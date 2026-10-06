using System.Collections.Generic;
using System.Linq;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Phase 14, slaves/spawns/containers: drones, the angry mob, hives, the propaganda tower, riders, overlords, hackers, troop crawlers and avalanche rocks.</summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> SlaveFiles() => new Dictionary<string, string>(Phase5Files())
    {
        ["Data/INI/Object/SlaveFixture.ini"] = """
        Locomotor SvDroneLoco
          Surfaces = AIR
          Speed = 60.0
          Acceleration = 600.0
          Braking = 600.0
          TurnRate = 360.0
          MinSpeed = 0.0
          PreferredHeight = 20.0
          CloseEnoughDist = 1.0
        End
        Locomotor SvFastLoco
          Speed = 240.0
          Acceleration = 900.0
          Braking = 900.0
          TurnRate = 360.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Weapon SvGun
          PrimaryDamage = 1
          AttackRange = 200.0
          DamageType = SMALL_ARMS
          DelayBetweenShots = 300
        End
        Weapon SvDeployGun
          PrimaryDamage = 0.00001
          AttackRange = 175.0
          DamageType = DEPLOY
          DelayBetweenShots = 1000
        End
        Upgrade Upgrade_SvSubliminal
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_SvFire
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        ObjectCreationList OCL_SvDrone
          CreateObject
            ObjectNames = SvScoutDrone
            Count = 1
            Disposition = LIKE_EXISTING
          End
        End

        Object SvMaster
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SvGun
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:10.0 Y:0.0 Z:0.0
            NaturalRallyPoint = X:20.0 Y:0.0 Z:0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 1
            SpawnReplaceDelay = 1000
            SpawnTemplateName = SvScoutDrone
          End
        End
        Object SvPlainMaster
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvRepairMaster
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:0.0 Y:10.0 Z:0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 1
            SpawnTemplateName = SvBattleDrone
          End
        End
        Object SvScoutDrone
          KindOf = SELECTABLE VEHICLE AIRCRAFT
          Locomotor = SET_NORMAL SvDroneLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlavedUpdate Slaved01
            GuardMaxRange = 35
            GuardWanderRange = 35
            AttackRange = 75
            AttackWanderRange = 10
            ScoutRange = 75
            ScoutWanderRange = 10
            DistToTargetToGrantRangeBonus = 30
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvBattleDrone
          KindOf = SELECTABLE VEHICLE AIRCRAFT
          Locomotor = SET_NORMAL SvDroneLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlavedUpdate Slaved01
            GuardMaxRange = 35
            GuardWanderRange = 35
            RepairRange = 8
            RepairMinAltitude = 18.0
            RepairMaxAltitude = 24.0
            RepairRatePerSecond = 30.0
            RepairWhenBelowHealth% = 60
            RepairMinReadyTime = 100
            RepairMaxReadyTime = 200
            RepairMinWeldTime = 100
            RepairMaxWeldTime = 200
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvEnemy
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SvNexus
          KindOf = INFANTRY MOB_NEXUS SELECTABLE
          Locomotor = SET_NORMAL SvFastLoco
          Body = ImmortalBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = QueueProductionExitUpdate Exit01
            UnitCreatePoint = X:0.0 Y:0.0 Z:0.0
            NaturalRallyPoint = X:0.0 Y:0.0 Z:0.0
            ExitDelay = 1000
            InitialBurst = 2
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 4
            SpawnReplaceDelay = 100000
            SpawnTemplateName = SvMobMember
            ExitByBudding = Yes
            InitialBurst = 2
            OneShot = No
            AggregateHealth = Yes
            SlavesHaveFreeWill = No
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvMobMember
          KindOf = INFANTRY SELECTABLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = MobMemberSlavedUpdate Mob01
            MustCatchUpRadius = 40
            NoNeedToCatchUpRadius = 15
            Squirrelliness = 0.05
            CatchUpCrisisBailTime = 3
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SvHive
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = HiveStructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
            PropagateDamageTypesToSlavesWhenExisting = NONE +SMALL_ARMS
            SwallowDamageTypesIfSlavesNotExisting = NONE +SMALL_ARMS
          End
          Behavior = SpawnPointProductionExitUpdate Exit01
            SpawnPointBoneName = SpawnPoint
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 2
            SpawnReplaceDelay = 500
            SpawnTemplateName = SvHiveSoldier
            SpawnedRequireSpawner = Yes
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvHiveSoldier
          KindOf = INFANTRY SELECTABLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 30.0
            InitialHealth = 30.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlavedUpdate Slaved01
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SvTower
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = PropagandaTowerBehavior Prop01
            Radius = 100.0
            DelayBetweenUpdates = 300
            HealPercentEachSecond = 30%
            UpgradedHealPercentEachSecond = 60%
            UpgradeRequired = Upgrade_SvSubliminal
            AffectsSelf = No
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvPropSoldier
          KindOf = INFANTRY SELECTABLE SCORE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SvGun
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SvBike
          KindOf = VEHICLE SELECTABLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = RiderChangeContain Rider01
            Slots = 1
            DamagePercentToUnits = 100%
            AllowInsideKindOf = INFANTRY
            InitialPayload = SvBikeRebel 1
            Rider1 = SvBikeRebel RIDER1 WEAPON_RIDER1 STATUS_RIDER1 SvBikeCommands SET_NORMAL
            Rider2 = SvBikeWorker RIDER2 WEAPON_RIDER2 STATUS_RIDER2 SvBikeCommands SET_NORMAL
            ScuttleDelay = 600
            ScuttleStatus = TOPPLED
            ExitDelay = 250
            GoAggressiveOnExit = Yes
            HealthRegen%PerSec = 0
          End
        End
        Object SvBikeRebel
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvBikeWorker
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvStranger
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SvOverlord
          KindOf = VEHICLE SELECTABLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 400.0
            InitialHealth = 400.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = OverlordContain Over01
            Slots = 1
            DamagePercentToUnits = 100%
            AllowInsideKindOf = PORTABLE_STRUCTURE
            PassengersAllowedToFire = Yes
            PassengersInTurret = Yes
            ExperienceSinkForRider = Yes
            PayloadTemplateName = SvBunker
          End
        End
        Object SvBunker
          KindOf = STRUCTURE PORTABLE_STRUCTURE SELECTABLE
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = TransportContain Bunker01
            Slots = 3
            AllowInsideKindOf = INFANTRY
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SvHackCenter
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = InternetHackContain Hack01
            Slots = 4
            AllowInsideKindOf = MONEY_HACKER
            ExitDelay = 500
            NumberOfExitPaths = 1
            PassengersAllowedToFire = No
            HealthRegen%PerSec = 10
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvHacker
          KindOf = INFANTRY SELECTABLE MONEY_HACKER
          TransportSlotCount = 1
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = HackInternetAIUpdate Hack01
            UnpackTime = 300
            PackTime = 300
            PackUnpackVariationFactor = 0.0
            CashUpdateDelay = 1000
            CashUpdateDelayFast = 500
            RegularCashAmount = 5
            VeteranCashAmount = 6
            EliteCashAmount = 8
            HeroicCashAmount = 10
            XpPerCashUpdate = 1
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SvFireTransport
          KindOf = VEHICLE SELECTABLE TRANSPORT
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = TransportContain Transport01
            Slots = 2
            AllowInsideKindOf = INFANTRY
          End
          Behavior = PassengersFireUpgrade Fire01
            TriggeredBy = Upgrade_SvFire
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SvCrawler
          KindOf = VEHICLE SELECTABLE TRANSPORT
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SvDeployGun
          End
          Behavior = AssaultTransportAIUpdate Assault01
            MembersGetHealedAtLifeRatio = 0.5
          End
          Behavior = TransportContain Transport01
            Slots = 4
            AllowInsideKindOf = INFANTRY
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvTrooper
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SvBunkerTarget
          KindOf = STRUCTURE SELECTABLE
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SvRock
          KindOf = SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = TensileFormationUpdate Tensile01
            Enabled = No
            CrackSound = AvalancheCrack
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static SimulationWorld SlaveWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: SlaveFiles());
        var world = NewWorld(fixture);
        feeder = new Feeder(world);
        return world;
    }

    private static Entity SlaveEntity(SimulationWorld world, string template, int nth = 0) => world.FindEntity(new EntityId(IdOf(world, template, nth)))!;

    private static void StepN(SimulationWorld world, int frames) // original frames (1/30 s of game time)
    {
        for (var tick = 0; tick < world.Frames(frames); tick++) world.Step();
    }

    private static T ModuleOf<T>(Entity entity) where T : class => entity.Modules.OfType<T>().Single();

    private static Fixed Gap2D(Entity a, Entity b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Fixed.Sqrt(dx * dx + dy * dy);
    }

    public static void SlaveFixtureObjectsAreInstantiable()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SlaveFiles());
        foreach (var name in new[] { "SvMaster", "SvScoutDrone", "SvBattleDrone", "SvNexus", "SvMobMember", "SvHive", "SvHiveSoldier", "SvTower", "SvBike", "SvOverlord", "SvBunker",
                     "SvHackCenter", "SvHacker", "SvFireTransport", "SvCrawler", "SvRock" })
            Check.That(fixture.Templates.TryGet(name, out var template) && template.IsInstantiable, $"{name} is instantiable: " + (template is null ? "missing" : string.Join(", ", template.MissingModules)));
        // A bad rider line is a data error, never a silent default.
        var broken = new Dictionary<string, string>(SlaveFiles())
        {
            ["Data/INI/Object/SlaveBroken.ini"] = """
            Object SvBrokenBike
              KindOf = VEHICLE
              Body = ActiveBody Body01
                MaxHealth = 10.0
                InitialHealth = 10.0
              End
              Behavior = RiderChangeContain Rider01
                Slots = 1
                AllowInsideKindOf = INFANTRY
                Rider1 = SvBikeRebel RIDER1 WEAPON_RIDER1 STATUS_NO_SUCH_THING SvBikeCommands SET_NORMAL
              End
            End
            """,
        };
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: broken), "STATUS_NO_SUCH_THING");
    }

    /// <summary>SlavedUpdate: a spawned drone is enslaved at once, follows its master, and is released when the master dies; a master without a spawner loses its flying drone.</summary>
    public static void SlavedUpdateFollowsItsMaster()
    {
        var world = SlaveWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "SvMaster", x: 100, y: 100);
        StepN(world, 40);
        var master = SlaveEntity(world, "SvMaster");
        var drone = SlaveEntity(world, "SvScoutDrone");
        var slaved = ModuleOf<SlavedUpdate>(drone);
        Check.That(slaved.Slaver == master.Id, "The spawner enslaved its drone as soon as it existed.");
        Check.That(drone.HasStatus(ObjectStatusBits.Index("UNSELECTABLE")), "An enslaved object cannot be selected.");
        Check.That(Gap2D(drone, master) < Fixed.FromInt(90), $"The drone hovers within its stray distance of an idle master ({Gap2D(drone, master)}).");

        feeder.SetMoveTarget(1, master.Id.Value, 420, 100);
        StepN(world, 150);
        Check.That(master.X > Fixed.FromInt(300), $"The master drove off ({master.X}).");
        Check.That(Gap2D(drone, master) < Fixed.FromInt(120), $"The drone kept up with the master ({Gap2D(drone, master)}).");
        Check.That(drone.X > Fixed.FromInt(200), "It followed instead of staying behind.");
        SnapshotContinues(world, fixture);

        // The spawner dies: the drone is released (onSlaverDie) and can be selected again.
        feeder.Kill(master.Id.Value);
        StepN(world, 3);
        Check.That(!slaved.Slaver.IsValid && !drone.HasStatus(ObjectStatusBits.Index("UNSELECTABLE")), "A slave of a dead spawner is released.");
    }

    /// <summary>SlavedUpdate through a creation list: enslaved by the nugget, killed when the master is gone (unmanned flier); deserter slaves change sides with the master.</summary>
    public static void SlavedUpdateCreationListAndUnmanned()
    {
        var world = SlaveWorld(out _, out var feeder);
        feeder.Spawn(1, "SvPlainMaster", x: 100, y: 100);
        world.Step();
        var master = SlaveEntity(world, "SvPlainMaster");
        world.CreateFromList(master, "OCL_SvDrone", EntityId.Invalid, false);
        StepN(world, 3);
        var drone = SlaveEntity(world, "SvScoutDrone");
        Check.That(ModuleOf<SlavedUpdate>(drone).Slaver == master.Id, "A creation list enslaves what it creates.");

        // The master changes sides: the drone defects with it.
        world.TransferOwnership(master.Id, new PlayerId(2));
        StepN(world, 20);
        Check.That(drone.Owner == new PlayerId(2), "A slave defects with its master.");

        world.RequestDestroy(master.Id);
        StepN(world, 20);
        Check.That(drone.EffectivelyDead || world.FindEntity(drone.Id) is null, "A flying slave whose master is gone crashes.");
    }

    /// <summary>The battle drone repairs a hurt master over time, without raising it above its maximum.</summary>
    public static void SlavedBattleDroneRepairsItsMaster()
    {
        var world = SlaveWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "SvRepairMaster", x: 100, y: 100);
        StepN(world, 20);
        var master = SlaveEntity(world, "SvRepairMaster");
        var drone = SlaveEntity(world, "SvBattleDrone");
        feeder.Damage(master.Id.Value, 70);
        world.Step();
        Check.That(master.Body!.Health == Fixed.FromInt(30), "The master is at 30 of 100, below the 60 % threshold.");
        var repaired = false;
        for (var tick = 0; tick < world.Frames(600) && !repaired; tick++)
        {
            world.Step();
            repaired = master.Body.Health > Fixed.FromInt(45);
        }
        Check.That(repaired, $"The drone repaired its master (health {master.Body.Health}).");
        Check.That(ModuleOf<SlavedUpdate>(drone).IsRepairing || master.Body.Health > Fixed.FromInt(45), "The drone went through its welding states.");
        SnapshotContinues(world, fixture);
        StepN(world, 1500);
        Check.That(master.Body.Health == master.Body.MaxHealth, $"Repair stops at full health ({master.Body.Health}).");
    }

    /// <summary>A scout drone near its master's victim grants the DRONE_SPOTTING bonus (and only then).</summary>
    public static void SlavedScoutDroneGrantsSpottingBonus()
    {
        var world = SlaveWorld(out _, out var feeder);
        feeder.Spawn(1, "SvMaster", x: 100, y: 100);
        feeder.Spawn(2, "SvEnemy", x: 160, y: 100);
        StepN(world, 20);
        var master = SlaveEntity(world, "SvMaster");
        var enemy = SlaveEntity(world, "SvEnemy");
        var bit = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("DRONE_SPOTTING", out var index) ? index : throw new InvalidOperationException("no DRONE_SPOTTING");
        bool Spotting() => (master.WeaponBonusConditions & (1u << bit)) != 0;
        Check.That(!Spotting(), "Without an attack there is no spotting bonus.");
        var granted = false;
        for (var tick = 0; tick < world.Frames(200) && !granted; tick++)
        {
            if (tick % 12 == 0) feeder.FireWeapon(1, master.Id.Value, enemy.Id.Value);
            world.Step();
            granted = Spotting();
        }
        Check.That(granted, "The drone flew to the victim and granted the master its spotting bonus.");
        StepN(world, 150);
        Check.That(!Spotting(), "Once the attack is over the bonus is gone again.");
    }

    /// <summary>The angry mob: budding spawns, aggregate health and veterancy, masked nexus, members that follow, bail out and die with it.</summary>
    public static void SpawnBehaviorMobNexus()
    {
        var world = SlaveWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "SvNexus", x: 200, y: 100);
        StepN(world, 220);
        var nexus = SlaveEntity(world, "SvNexus");
        var members = world.Entities.Where(value => value.Template.Name == "SvMobMember").OrderBy(value => value.Id.Value).ToArray();
        Check.That(members.Length == 4, $"The nexus produced its four members ({members.Length}).");
        Check.That(members.All(member => ModuleOf<MobMemberSlavedUpdate>(member).Slaver == nexus.Id), "Every member was enslaved to the nexus.");
        Check.That(members.All(member => Gap2D(member, nexus) < Fixed.FromInt(10)), "Budded members start on the nexus (or on a member standing there).");
        Check.That(nexus.HasStatus(ObjectStatusBits.Index("MASKED")), "A nexus \"doesn't exist\": enemies cannot shoot at it.");
        Check.That(nexus.Body!.Health == nexus.Body.MaxHealth, "A full mob is a full-health nexus.");

        // The nexus' health is the share of its members' health.
        feeder.Damage(members[0].Id.Value, 25);
        StepN(world, 2);
        Check.That(nexus.Body.Health == Fixed.FromInt(870), $"175 of 200 member health is 87 percent of 1000 ({nexus.Body.Health}).");
        SnapshotContinues(world, fixture);

        // A dead member is replaced only after the (long) replace delay; the other three keep the mob alive.
        feeder.Kill(members[1].Id.Value);
        StepN(world, 40);
        Check.That(world.Entities.Count(value => value.Template.Name == "SvMobMember" && !value.EffectivelyDead) == 3, "The dead member is not replaced yet.");

        // The nexus runs off faster than its members walk: they bail out one by one and the nexus dies with the last.
        feeder.SetMoveTarget(1, nexus.Id.Value, 560, 100);
        StepN(world, 400);
        Check.That(members.All(member => member.EffectivelyDead || world.FindEntity(member.Id) is null), "Members that cannot keep up with the nexus die.");
        Check.That(world.FindEntity(nexus.Id) is null || nexus.DestroyRequested, "The nexus is destroyed with its last member.");
    }

    /// <summary>A mob member without a nexus dies at its first check.</summary>
    public static void MobMemberWithoutNexusDies()
    {
        var world = SlaveWorld(out _, out var feeder);
        feeder.Spawn(1, "SvMobMember", x: 50, y: 50);
        world.Step();
        var member = SlaveEntity(world, "SvMobMember");
        StepN(world, 4);
        Check.That(member.EffectivelyDead, "An orphaned mob member kills itself.");
    }

    /// <summary>HiveStructureBody: the closest slave takes propagated damage, the hive takes the rest; swallowed without slaves. Spawn points hold distinct slaves and are reused.</summary>
    public static void HiveStructureBodyPassesDamageToSlaves()
    {
        var world = SlaveWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "SvHive", x: 200, y: 200);
        feeder.Spawn(2, "SvEnemy", x: 300, y: 200);
        StepN(world, 60);
        var hive = SlaveEntity(world, "SvHive");
        var enemy = SlaveEntity(world, "SvEnemy");
        var soldiers = world.Entities.Where(value => value.Template.Name == "SvHiveSoldier").OrderBy(value => value.Id.Value).ToArray();
        Check.That(soldiers.Length == 2, $"The hive spawned its two soldiers ({soldiers.Length}).");
        Check.That(soldiers.All(soldier => ModuleOf<SlavedUpdate>(soldier).Slaver == hive.Id), "The soldiers are slaves of the hive.");
        Check.That((soldiers[0].X != soldiers[1].X || soldiers[0].Y != soldiers[1].Y) && soldiers.All(soldier => (Gap2D(soldier, hive) - hive.Template.BoundingCircleRadius).Abs() < Fixed.One),
            "Each soldier stands on its own spawn point around the hive.");
        var points = soldiers.Select(soldier => (soldier.X, soldier.Y)).ToHashSet();

        var smallArms = NameTables.DamageType.TryIndexOf("SMALL_ARMS", out var small) ? small : throw new InvalidOperationException();
        var near = soldiers.OrderBy(soldier => Gap2D(soldier, enemy)).First();
        var far = soldiers.First(soldier => soldier != near);
        var hiveHealth = hive.Body!.Health;
        world.Damage(hive.Id, new DamageInfo(enemy.Id, Fixed.FromInt(10), smallArms, DeathTypes.Normal, false));
        Check.That(hive.Body.Health == hiveHealth, "Small-arms fire does not hurt the hive while it has slaves.");
        Check.That(near.Body!.Health == Fixed.FromInt(20) && far.Body!.Health == Fixed.FromInt(30), "The slave closest to the shooter took it.");
        world.Damage(hive.Id, new DamageInfo(enemy.Id, Fixed.FromInt(10), DamageTypes.Explosion, DeathTypes.Normal, false));
        Check.That(hive.Body.Health == hiveHealth - Fixed.FromInt(10), "A damage type that is not propagated hurts the hive.");
        world.Damage(hive.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(10), smallArms, DeathTypes.Normal, false));
        Check.That(hive.Body.Health == hiveHealth - Fixed.FromInt(20), "Damage without a known shooter cannot be redirected.");
        SnapshotContinues(world, fixture);

        // A killed slave is replaced on the freed spawn point.
        var freed = (near.X, near.Y);
        world.Kill(near.Id);
        StepN(world, 80);
        var living = world.Entities.Where(value => value.Template.Name == "SvHiveSoldier" && !value.EffectivelyDead).ToArray();
        Check.That(living.Length == 2 && living.Select(soldier => (soldier.X, soldier.Y)).ToHashSet().SetEquals(points) && living.Any(soldier => (soldier.X, soldier.Y) == freed), "The replacement stands on the spawn point that was freed.");

        // Without any slave the swallowed type does nothing at all.
        foreach (var soldier in living) world.Kill(soldier.Id);
        StepN(world, 2);
        var before = hive.Body.Health;
        var outcome = world.Damage(hive.Id, new DamageInfo(enemy.Id, Fixed.FromInt(10), smallArms, DeathTypes.Normal, false));
        Check.That(outcome == DamageOutcome.NoEffect && hive.Body.Health == before, "A swallowed damage type is eaten when the hive has no slave left.");
    }

    /// <summary>PropagandaTowerBehavior: allies in range get the bonus and heal, others do not; towers do not stack; the upgrade adds SUBLIMINAL; the death removes it all.</summary>
    public static void PropagandaTowerBuffsAlliesInRange()
    {
        var world = SlaveWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "SvTower", x: 100, y: 100);
        feeder.Spawn(1, "SvPropSoldier", x: 130, y: 100);
        feeder.Spawn(1, "SvPropSoldier", x: 400, y: 100);
        feeder.Spawn(2, "SvPropSoldier", x: 120, y: 100);
        StepN(world, 2);
        var tower = SlaveEntity(world, "SvTower");
        var inside = SlaveEntity(world, "SvPropSoldier", 0);
        var outside = SlaveEntity(world, "SvPropSoldier", 1);
        var enemy = SlaveEntity(world, "SvPropSoldier", 2);
        var enthusiastic = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("ENTHUSIASTIC", out var e) ? e : throw new InvalidOperationException();
        var subliminal = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("SUBLIMINAL", out var s) ? s : throw new InvalidOperationException();
        bool Has(Entity entity, int bit) => (entity.WeaponBonusConditions & (1u << bit)) != 0;
        Check.That(!Has(inside, enthusiastic), "Before the first scan nobody is covered.");
        feeder.Damage(inside.Id.Value, 60);
        world.Step();
        var start = inside.Body!.Health;
        StepN(world, 40);
        Check.That(Has(inside, enthusiastic) && !Has(inside, subliminal), "The ally in range is enthusiastic.");
        Check.That(!Has(outside, enthusiastic) && !Has(enemy, enthusiastic), "The ally out of range and the enemy are not.");
        Check.That(inside.Body.Health > start, $"The ally is healed ({start} -> {inside.Body.Health}).");
        SnapshotContinues(world, fixture);

        // Healing is 30 % of 100 per second: one a tick, and a second tower on top does not double it.
        feeder.Damage(inside.Id.Value, 30);
        world.Step();
        var single = inside.Body.Health;
        StepN(world, 20);
        var gainSingle = inside.Body.Health - single;
        feeder.Spawn(1, "SvTower", x: 110, y: 100);
        StepN(world, 20);
        feeder.Damage(inside.Id.Value, 30);
        world.Step();
        var double1 = inside.Body.Health;
        StepN(world, 20);
        var gainDouble = inside.Body.Health - double1;
        Check.That(gainSingle >= Fixed.FromInt(15) && gainSingle <= Fixed.FromInt(25), $"One tower heals about a point a tick ({gainSingle} in 20).");
        Check.That(gainDouble <= gainSingle + Fixed.FromInt(2), $"Two towers do not stack ({gainDouble} against {gainSingle}).");

        // The player's upgrade adds the subliminal bonus.
        world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_SvSubliminal");
        StepN(world, 20);
        Check.That(Has(inside, subliminal), "With the subliminal messaging upgrade the ally gets that bonus too.");

        // All towers die: the bonuses go with them.
        foreach (var t in world.Entities.Where(value => value.Template.Name == "SvTower").ToArray()) world.Kill(t.Id);
        StepN(world, 3);
        Check.That(!Has(inside, enthusiastic) && !Has(inside, subliminal), "Without a tower the bonuses are removed.");
        Check.That(tower.EffectivelyDead, "The tower itself died.");
    }

    /// <summary>RiderChangeContain: the initial rider boards, a new rider replaces it, a stranger is refused, a deserted bike scuttles and dies, a dead bike kills its rider.</summary>
    public static void RiderChangeContainSwapsRiders()
    {
        var world = SlaveWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "SvBike", x: 100, y: 100);
        StepN(world, 6);
        var bike = SlaveEntity(world, "SvBike");
        var rebel = SlaveEntity(world, "SvBikeRebel");
        var riderOne = ObjectStatusBits.Index("STATUS_RIDER1");
        var riderTwo = ObjectStatusBits.Index("STATUS_RIDER2");
        Check.That(rebel.ContainedBy == bike.Id && bike.Container!.ContainCount == 1, "The initial payload rides the bike.");
        Check.That(bike.HasStatus(riderOne) && !bike.HasStatus(riderTwo), "The rider's object status is set on the bike.");
        SnapshotContinues(world, fixture);

        feeder.Spawn(1, "SvStranger", x: 100, y: 100);
        feeder.Spawn(1, "SvBikeWorker", x: 100, y: 100);
        world.Step();
        var stranger = SlaveEntity(world, "SvStranger");
        var worker = SlaveEntity(world, "SvBikeWorker");
        Check.That(!world.TryEnterContainer(stranger, bike), "A type that no rider line names cannot board.");
        Check.That(world.TryEnterContainer(worker, bike), "A listed rider boards even though the bike is full.");
        Check.That(!rebel.ContainedBy.IsValid && rebel.Body!.Health == rebel.Body.MaxHealth && !rebel.EffectivelyDead, "The old rider was thrown out alive.");
        Check.That(bike.Container!.ContainCount == 1 && worker.ContainedBy == bike.Id, "The new rider drives the bike.");
        Check.That(bike.HasStatus(riderTwo) && !bike.HasStatus(riderOne), "The status follows the rider.");
        Check.That(!ModuleOf<RiderChangeContain>(bike).IsScuttled && !bike.HasStatus(ObjectStatusBits.Index("UNSELECTABLE")), "A replaced rider does not scuttle the bike.");

        // The last rider gets off: the bike is scuttled and, 18 ticks later, dies.
        world.EvacuateContainer(bike);
        world.Step();
        Check.That(ModuleOf<RiderChangeContain>(bike).IsScuttled && bike.HasStatus(ObjectStatusBits.Index("UNSELECTABLE")) && bike.HasStatus(ObjectStatusBits.Index("IMMOBILE")), "A deserted bike is scuttled: unselectable and immobile.");
        Check.That(!world.TryEnterContainer(worker, bike), "Nobody boards a scuttled bike.");
        StepN(world, 25);
        Check.That(bike.EffectivelyDead, "The scuttled bike topples and dies.");

        // A bike that dies with a rider takes it along.
        feeder.Spawn(1, "SvBike", x: 300, y: 300);
        StepN(world, 6);
        var second = world.Entities.Where(value => value.Template.Name == "SvBike" && !value.EffectivelyDead).Single();
        var passenger = world.Entities.Where(value => value.ContainedBy == second.Id).Single();
        world.Kill(second.Id);
        StepN(world, 3);
        Check.That(passenger.EffectivelyDead || world.FindEntity(passenger.Id) is null, "The rider of a destroyed bike is destroyed with it.");
    }

    /// <summary>OverlordContain: the payload structure is created inside, passes queries to its own container, follows the Overlord and dies with it.</summary>
    public static void OverlordContainRedirectsToItsStructure()
    {
        var world = SlaveWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "SvOverlord", x: 100, y: 100);
        StepN(world, 6);
        var overlord = SlaveEntity(world, "SvOverlord");
        var bunker = SlaveEntity(world, "SvBunker");
        Check.That(bunker.ContainedBy == overlord.Id, "The payload structure sits on the Overlord.");
        var contain = ModuleOf<OverlordContain>(overlord);
        Check.That(contain.Redirecting && overlord.Container!.ContainMax == 3, "With a structure aboard, the Overlord reports the structure's capacity.");

        feeder.Spawn(1, "SvBikeRebel", x: 100, y: 100);
        world.Step();
        var infantry = SlaveEntity(world, "SvBikeRebel");
        Check.That(world.TryEnterContainer(infantry, overlord), "Infantry enter the Overlord (its bunker).");
        Check.That(bunker.Container!.ContainCount == 1 && overlord.Container!.ContainCount == 1 && overlord.Container.Contained.Single() == infantry.Id, "The passenger is listed by the structure, the Overlord shows the same list.");
        SnapshotContinues(world, fixture);

        // The structure follows the Overlord.
        feeder.SetMoveTarget(1, overlord.Id.Value, 300, 100);
        StepN(world, 60);
        Check.That(overlord.X > Fixed.FromInt(150) && (bunker.X - overlord.X).Abs() < Fixed.FromInt(3) && (bunker.Y - overlord.Y).Abs() < Fixed.FromInt(3), "The structure rides along.");

        // Evacuating lets out the infantry, never the structure.
        world.EvacuateContainer(overlord);
        Check.That(!infantry.ContainedBy.IsValid && bunker.ContainedBy == overlord.Id, "Only the passengers leave.");

        // The Overlord dies: the structure dies first.
        world.Kill(overlord.Id);
        StepN(world, 2);
        Check.That(bunker.EffectivelyDead, "The structure dies with the Overlord.");
    }

    /// <summary>InternetHackContain, HackInternetAIUpdate and HiveStructureBody together: a hacker earns cash inside the center, outside it packs up before moving.</summary>
    public static void HackersEarnCashInsideTheInternetCenter()
    {
        var world = SlaveWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "SvHackCenter", x: 100, y: 100);
        feeder.Spawn(1, "SvHacker", x: 100, y: 100);
        feeder.Spawn(1, "SvStranger", x: 100, y: 100);
        StepN(world, 2);
        var center = SlaveEntity(world, "SvHackCenter");
        var hacker = SlaveEntity(world, "SvHacker");
        var hack = ModuleOf<HackInternetAIUpdate>(hacker);
        Check.That(!world.TryEnterContainer(SlaveEntity(world, "SvStranger"), center), "Only money hackers fit into the Internet Center.");
        var money = world.MoneyOf(new PlayerId(1));
        Check.That(world.TryEnterContainer(hacker, center), "The hacker enters.");
        Check.That(hack.IsHackingPackingOrUnpacking, "Entering the center is the order to hack.");
        StepN(world, 12);
        Check.That(hack.IsHacking, "After the unpack time the hacker is hacking.");
        var afterUnpack = world.MoneyOf(new PlayerId(1));
        StepN(world, 62);
        var earned = world.MoneyOf(new PlayerId(1)) - afterUnpack;
        // CashUpdateDelayFast 500 ms = 15 ticks, one update more for the payout itself: 4 payouts in 62 ticks.
        Check.That(earned == Fixed.FromInt(20), $"A contained hacker is paid every 16th tick (earned {earned}).");
        Check.That(hacker.Experience >= 4 || !hacker.Template.IsTrainable, "The experience per payout is added (for trainable hackers).");
        SnapshotContinues(world, fixture);

        // Out of the building: the slow delay, and a move order waits for the hacker to pack.
        center.Container!.Remove(world, hacker, true);
        hack.StartHacking(world);
        StepN(world, 12);
        var outside = world.MoneyOf(new PlayerId(1));
        StepN(world, 62);
        Check.That(world.MoneyOf(new PlayerId(1)) - outside == Fixed.FromInt(10), $"On the ground it is paid every 31st tick ({world.MoneyOf(new PlayerId(1)) - outside}).");
        feeder.SetMoveTarget(1, hacker.Id.Value, 300, 100);
        world.Step();
        Check.That(!hacker.HasMoveTarget, "A hacker packs up before it moves.");
        StepN(world, 14);
        Check.That(hacker.HasMoveTarget || hacker.X > Fixed.FromInt(105), "After packing the held order is carried out.");
        var settled = world.MoneyOf(new PlayerId(1));
        StepN(world, 40);
        Check.That(world.MoneyOf(new PlayerId(1)) == settled, "A moving hacker earns nothing.");
    }

    /// <summary>PassengersFireUpgrade records the permission on the container.</summary>
    public static void PassengersFireUpgradeSetsTheFlag()
    {
        var world = SlaveWorld(out _, out var feeder);
        feeder.Spawn(1, "SvFireTransport", x: 50, y: 50);
        world.Step();
        var transport = SlaveEntity(world, "SvFireTransport");
        var contain = (ContainModuleBase)transport.Container!;
        Check.That(!contain.PassengersAllowedToFire, "Passengers may not fire at first.");
        world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_SvFire");
        Check.That(contain.PassengersAllowedToFire, "The upgrade lets them fire.");
        Check.That(ModuleOf<PassengersFireUpgrade>(transport).Executed, "The upgrade module is marked executed.");
    }

    /// <summary>AssaultTransportAIUpdate: the deploy shot sends the healthy troops out against the nearest enemy; wounded troops stay, late arrivals sit out the assault.</summary>
    public static void AssaultTransportDeploysItsTroops()
    {
        var world = SlaveWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "SvCrawler", x: 100, y: 100);
        for (var index = 0; index < 3; index++) feeder.Spawn(1, "SvTrooper", x: 100, y: 100);
        feeder.Spawn(2, "SvBunkerTarget", x: 200, y: 100);
        StepN(world, 2);
        var crawler = SlaveEntity(world, "SvCrawler");
        var target = SlaveEntity(world, "SvBunkerTarget");
        var troopers = world.Entities.Where(value => value.Template.Name == "SvTrooper").OrderBy(value => value.Id.Value).ToArray();
        foreach (var trooper in troopers.Take(2)) Check.That(world.TryEnterContainer(trooper, crawler), "A trooper boards the crawler.");
        feeder.Damage(troopers[1].Id.Value, 70);
        StepN(world, 2);
        var assault = ModuleOf<AssaultTransportAIUpdate>(crawler);
        Check.That(assault.MemberCount == 2, "The boarded troops are the transport's members.");

        Check.That(feeder.FireWeapon(1, crawler.Id.Value, target.Id.Value).Accepted, "The transport's deploy weapon fires.");
        StepN(world, 6);
        Check.That(assault.DesignatedTarget == target.Id, "The shot designated the nearest enemy.");
        Check.That(!troopers[0].ContainedBy.IsValid, "The healthy member got out.");
        Check.That(troopers[1].ContainedBy == crawler.Id, "The wounded member stays inside to be healed.");
        Check.That(world.TryEnterContainer(troopers[2], crawler), "A third trooper boards while the assault is on.");
        StepN(world, 4);
        Check.That(troopers[2].ContainedBy == crawler.Id, "A member that boarded after the assault began sits it out.");
        var gap = Gap2D(troopers[0], target);
        StepN(world, 30);
        Check.That(Gap2D(troopers[0], target) < gap, "The deployed trooper advances on the target.");
        SnapshotContinues(world, fixture);
    }

    /// <summary>TensileFormationUpdate: a hurt rock sets its neighbours sliding down the slope; the formation stays together and stops after 300 ticks.</summary>
    public static void TensileFormationSlidesDownTheSlope()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SlaveFiles());
        var map = SyntheticMaps.Build(60, 40, 1, height: (x, _) => x);
        var terrain = TerrainMap.Build(map, "slope");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "SvRock", x: 300, y: 200);
        feeder.Spawn(1, "SvRock", x: 310, y: 200);
        feeder.Spawn(1, "SvRock", x: 320, y: 215);
        StepN(world, 100);
        var rocks = world.Entities.Where(value => value.Template.Name == "SvRock").OrderBy(value => value.Id.Value).ToArray();
        var start = rocks.Select(rock => rock.X).ToArray();
        Check.That(rocks.All(rock => !ModuleOf<TensileFormationUpdate>(rock).Enabled), "An undamaged formation sits still.");
        Check.That(rocks.Select((rock, index) => rock.X == start[index]).All(same => same), "Nothing moves before a rock is hurt.");

        feeder.Damage(rocks[0].Id.Value, 60);
        StepN(world, 200);
        Check.That(rocks.All(rock => ModuleOf<TensileFormationUpdate>(rock).Enabled), "Hurting one rock set the whole formation moving.");
        Check.That(rocks.Select((rock, index) => start[index] - rock.X).All(moved => moved > Fixed.FromInt(3)), $"Every rock slid downhill (towards smaller x): {string.Join(", ", rocks.Select((rock, index) => start[index] - rock.X))}.");
        Check.That(Gap2D(rocks[0], rocks[1]) < Fixed.FromInt(30), "The tied rocks slid together.");
        var snapshot = WorldSnapshot.Write(world);
        var restored = WorldSnapshot.Restore(snapshot, fixture.Templates, fixture.Fingerprint, terrain);
        Check.That(restored.StateHash == world.StateHash, "A sliding formation round-trips.");
        StepN(world, 250);
        var resting = rocks.Select(rock => rock.X).ToArray();
        StepN(world, 30);
        Check.That(rocks.Select((rock, index) => rock.X == resting[index]).All(same => same), "After 300 ticks the rocks stay where they are.");
    }
}

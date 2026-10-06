using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.Definitions;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
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

/// <summary>The structures-and-world module group: upgrade die, collapsing buildings, burning trees, deploy-style units, trains, traps and mines, bridges and the world effect modules.</summary>
static partial class Tests
{
    private const string StructuresFixtureIni = """
        GameData
          Gravity = -64.0
        End
        Upgrade Upgrade_SwDrone
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_SwCheap
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        ObjectCreationList OCL_SwDrone
          CreateObject
            ObjectNames = SwDrone
          End
        End
        ObjectCreationList OCL_SwMine
          CreateObject
            ObjectNames = SwMine
          End
        End
        ObjectCreationList OCL_SwDebris
          CreateObject
            ObjectNames = SwDebris
          End
        End
        Weapon SwFlameTouch
          PrimaryDamage = 7
          AttackRange = 5.0
          DamageType = FLAME
          DelayBetweenShots = 100
        End
        Weapon SwDeployGun
          PrimaryDamage = 10
          AttackRange = 100.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 100
        End
        Weapon SwBlast
          PrimaryDamage = 50
          PrimaryDamageRadius = 30.0
          RadiusDamageAffects = ENEMIES
          AttackRange = 10.0
          DamageType = EXPLOSION
          DelayBetweenShots = 100
        End
        Weapon SwMineWeapon
          PrimaryDamage = 30
          PrimaryDamageRadius = 12.0
          RadiusDamageAffects = ENEMIES
          AttackRange = 1.0
          DamageType = EXPLOSION
          DelayBetweenShots = 100
        End
        Object SwDebris
          KindOf = NONE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = LifetimeUpdate Life01
            MinLifetime = 6000
            MaxLifetime = 6000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwDroneCarrier
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = ObjectCreationUpgrade Upg01
            TriggeredBy = Upgrade_SwDrone
            UpgradeObject = OCL_SwDrone
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwDrone
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = UpgradeDie Die01
            DeathTypes = ALL
            UpgradeToRemove = Upgrade_SwDrone ModuleTag_13
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwBuilding
          KindOf = STRUCTURE IMMOBILE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 20.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = StructureCollapseUpdate Collapse01
            MinCollapseDelay = 300
            MaxCollapseDelay = 300
            CollapseDamping = 0.0
            MaxShudder = 0.6
            MinBurstDelay = 100
            MaxBurstDelay = 100
            BigBurstFrequency = 1
            OCL = BURST OCL_SwDebris
            OCL = FINAL OCL_SwDebris
            FXList = INITIAL FX_Test
            FXList = FINAL FX_Other
          End
          Behavior = KeepObjectDie Keep01
          End
        End
        Object SwTree
          KindOf = NONE
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryHeight = 20.0
          GeometryIsSmall = Yes
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = FlammableUpdate Flame01
            AflameDuration = 3000
            AflameDamageAmount = 1
            AflameDamageDelay = 500
            FlameDamageLimit = 10
            FlameDamageExpiration = 5000
          End
          Behavior = FireSpreadUpdate Spread01
            OCLEmbers = OCL_SwDebris
            MinSpreadDelay = 500
            MaxSpreadDelay = 500
            SpreadTryRange = 30
          End
          Behavior = FireWeaponCollide Collide01
            CollideWeapon = SwFlameTouch
            RequiredStatus = AFLAME
          End
        End
        Object SwWalker
          KindOf = SELECTABLE INFANTRY
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwHeavy
          KindOf = SELECTABLE VEHICLE
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 40.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwDeployer
          KindOf = SELECTABLE VEHICLE
          VisionRange = 300.0
          ShroudClearingRange = 300.0
          Locomotor = SET_NORMAL SkirmishLoco
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SwDeployGun
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DeployStyleAIUpdate AI01
            Turret
              TurretTurnRate = 180
              AllowsPitch = No
              ControlledWeaponSlots = PRIMARY
            End
            AutoAcquireEnemiesWhenIdle = Yes
            PackTime = 500
            UnpackTime = 1000
            TurretsFunctionOnlyWhenDeployed = Yes
            TurretsMustCenterBeforePacking = Yes
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwTarget
          KindOf = SELECTABLE VEHICLE
          VisionRange = 50.0
          ShroudClearingRange = 50.0
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
        End
        Object SwLoco
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = RailroadBehavior Rail01
            PathPrefixName = Railroad
            RunningGarrisonSpeedMax = 1
            KillSpeedMin = 1
            Friction = 0.995
            IsLocomotive = Yes
            SpeedMax = 3
            Acceleration = 1.1
            WaitAtStationTime = 1000
            Braking = 0.9
            CarriageTemplateName = SwCar
            CarriageTemplateName = SwCar
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwCar
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = RailroadBehavior Rail01
            RunningGarrisonSpeedMax = 1
            KillSpeedMin = 1
            Friction = 0.995
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwPlant
          KindOf = STRUCTURE IMMOBILE FS_POWER
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwTrap
          KindOf = STRUCTURE IMMOBILE
          Body = StructureBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DemoTrapUpdate Trap01
            DefaultProximityMode = Yes
            DetonationWeaponSlot = PRIMARY
            ProximityModeWeaponSlot = SECONDARY
            ManualModeWeaponSlot = TERTIARY
            TriggerDetonationRange = 40.0
            IgnoreTargetTypes = PROJECTILE UNATTACKABLE
            AutoDetonationWithFriendsInvolved = Yes
            DetonationWeapon = SwBlast
            DetonateWhenKilled = Yes
          End
          Behavior = KeepObjectDie Keep01
          End
        End
        Object SwTrooper
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwMine
          KindOf = IMMOBILE MINE
          Geometry = CYLINDER
          GeometryMajorRadius = 8.0
          GeometryHeight = 1.0
          GeometryIsSmall = Yes
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = MinefieldBehavior Mine01
            DetonationWeapon = SwMineWeapon
            DetonatedBy = ENEMIES NEUTRAL
            RepeatDetonateMoveThresh = 5.0
            NumVirtualMines = 4
            Regenerates = Yes
            StopsRegenAfterCreatorDies = Yes
            CreatorDeathCheckRate = 1000
            DegenPercentPerSecondAfterCreatorDies = 20%
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 25
            HealingDelay = 1000
            StartHealingDelay = 1000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwMineLayer
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = ObjectCreationUpgrade Upg01
            TriggeredBy = Upgrade_SwDrone
            UpgradeObject = OCL_SwMine
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwBridge
          KindOf = STRUCTURE IMMOBILE BRIDGE
          Geometry = BOX
          GeometryMajorRadius = 40.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 5.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = BridgeBehavior Bridge01
            LateralScaffoldSpeed = 100.0
            VerticalScaffoldSpeed = 70.0
            BridgeDieFX = FX:FX_Test Delay: 0 Bone:Explosion01
            BridgeDieOCL = OCL:OCL_SwDebris Delay: 0
            BridgeDieOCL = OCL:OCL_SwDebris Delay: 1000 Bone:ParentObject
          End
          Behavior = KeepObjectDie Keep01
          End
        End
        Object SwBridgeTower
          KindOf = UNATTACKABLE STRUCTURE IMMOBILE BRIDGE_TOWER
          Geometry = BOX
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 30.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = BridgeTowerBehavior Tower01
          End
          Behavior = KeepObjectDie Keep01
          End
        End
        Object SwScaffold
          KindOf = NONE
          Behavior = BridgeScaffoldBehavior Scaffold01
          End
        End
        Object SwDropZone
          KindOf = STRUCTURE IMMOBILE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = OCLUpdate Drop01
            OCL = OCL_SwDebris
            MinDelay = 1000
            MaxDelay = 1000
            CreateAtEdge = No
          End
        End
        Object SwPad
          KindOf = STRUCTURE IMMOBILE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = OCLUpdate Pad01
            MinDelay = 1000
            MaxDelay = 1000
            FactionTriggered = Yes
            FactionOCL = Faction:Alpha OCL:OCL_SwDebris
          End
        End
        Object SwReveal
          KindOf = SELECTABLE
          ShroudClearingRange = 100.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DynamicShroudClearingRangeUpdate Shroud01
            FinalVision = 20.0
            ChangeInterval = 50
            ShrinkDelay = 2000
            ShrinkTime = 1000
            GrowDelay = 0
            GrowTime = 1000
            GrowInterval = 10
            GridDecalTemplate
              Texture = EXGrid
              Style = SHADOW_ADDITIVE_DECAL
            End
          End
        End
        Object SwBuoy
          KindOf = NONE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = FloatUpdate Float01
            Enabled = Yes
          End
        End
        Locomotor SwWaveLoco
          Surfaces = AIR
          Speed = 120.0
          Acceleration = 500.0
          Braking = 500.0
          TurnRate = 20.0
          PreferredHeight = 10.0
        End
        Object SwDam
          KindOf = STRUCTURE IMMOBILE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DamDie Dam01
          End
          Behavior = KeepObjectDie Keep01
          End
        End
        Object SwWave
          KindOf = WAVEGUIDE
          Locomotor = SET_NORMAL SwWaveLoco
          Body = ImmortalBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = WaveGuideUpdate Wave01
            WaveDelay = 3000
            YSize = 100.0
            LinearWaveSpacing = 15.0
            WaveBendMagnitude = 500.0
            WaterVelocity = 2.7
            PreferredHeight = 37.3
            ShorelineEffectDistance = 5.0
            DamageRadius = 25.0
            DamageAmount = 99999
            ToppleForce = 0.25
            RandomSplashSound = WaveRandomSplash
            RandomSplashSoundFrequency = 50
            BridgeParticle = WaveHitBridge01
            BridgeParticleAngleFudge = -95.0
            LoopingSound = DamBreakWaveLoop
          End
        End
        Object SwCheckpoint
          KindOf = STRUCTURE IMMOBILE
          VisionRange = 80.0
          Geometry = BOX
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = CheckpointUpdate Check01
            ScanDelayTime = 1000
          End
        End
        Object SwWatcher
          KindOf = STRUCTURE IMMOBILE
          VisionRange = 80.0
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = EnemyNearUpdate Near01
            ScanDelayTime = 100
          End
        End
        Object SwFirestorm
          KindOf = IMMOBILE NO_COLLIDE
          Geometry = CYLINDER
          GeometryMajorRadius = 1.0
          GeometryHeight = 10.0
          GeometryIsSmall = Yes
          Body = InactiveBody Body01
          End
          Behavior = FirestormDynamicGeometryInfoUpdate Storm01
            InitialDelay = 100
            InitialHeight = 10.0
            InitialMajorRadius = 1.0
            FinalHeight = 10.0
            FinalMajorRadius = 60.0
            TransitionTime = 1000
            ReverseAtTransitionTime = Yes
            ScorchSize = 100.0
            ParticleOffsetZ = 0.1
            ParticleSystem1 = FireStormMain
            FXList = FX_Test
            DelayBetweenDamageFrames = 500
            DamageAmount = 10.0
          End
        End
        Object SwBeacon
          KindOf = IMMOBILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = WeaponBonusUpdate Bonus01
            RequiredAffectKindOf = CAN_ATTACK
            ForbiddenAffectKindOf = STRUCTURE
            BonusDuration = 1000
            BonusDelay = 3000
            BonusRange = 100
            BonusConditionType = FRENZY_ONE
          End
        End
        Object SwShooter
          KindOf = SELECTABLE INFANTRY CAN_ATTACK
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object SwDiscountHall
          KindOf = STRUCTURE IMMOBILE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = CostModifierUpgrade Cost01
            TriggeredBy = Upgrade_SwCheap
            EffectKindOf = VEHICLE
            Percentage = -10%
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwBoned
          KindOf = STRUCTURE IMMOBILE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = BoneFXDamage Bone01
          End
          Behavior = BoneFXUpdate Bone02
            PristineParticleSystem1 = Bone:Smoke01 RandomBone:No PSys:SmokeA
            RubbleFXList1 = Bone:Fire02 OnlyOnce:Yes FXList:FX_Test
          End
        End
        Object SwBonedOcl
          KindOf = STRUCTURE IMMOBILE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = BoneFXDamage Bone01
          End
          Behavior = BoneFXUpdate Bone02
            RubbleOCL1 = Bone:Fire02 OCL:OCL_SwDebris
          End
        End
        """;

    private static readonly ImmutableArray<PlayerConfiguration> StructuresPlayers =
    [
        new(new PlayerId(1), "Alpha", "FactionAlpha", Team: 1),
        new(new PlayerId(2), "Beta", "FactionBeta", Team: 2),
        new(new PlayerId(3), "Gamma", null, Team: 1),
    ];

    private static IReadOnlyDictionary<string, string> StructuresFiles() => new Dictionary<string, string>(SkirmishFiles())
    {
        ["Data/INI/Object/StructuresFixture.ini"] = StructuresFixtureIni,
    };

    /// <summary>
    /// A 60 x 40 cell map with the waypoint paths the tests use: a train track (RailA to RailD, RailC is a station), a closed loop and the dam wave's path (WaveGuide1 to WaveGuide2).
    /// Waypoint ids are assigned in list order starting at 1.
    /// </summary>
    private static TerrainMap StructuresTerrain(bool lake = false)
    {
        var waypoints = new (string Name, decimal X, decimal Y, decimal Z)[]
        {
            ("RailA", 50m, 100m, 0m), ("RailB", 200m, 100m, 0m), ("RailCStation", 350m, 100m, 0m), ("RailD", 550m, 100m, 0m),
            ("LoopA", 300m, 300m, 0m), ("LoopB", 400m, 300m, 0m), ("LoopC", 400m, 350m, 0m), ("LoopD", 300m, 350m, 0m),
            ("WaveGuide1", 100m, 250m, 0m), ("WaveGuide2", 500m, 250m, 0m),
        };
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: waypoints, triggers: lake ? [SyntheticMaps.Water("Lake", 5, (0, 0), (100, 0), (100, 100), (0, 100))] : null);
        var links = ImmutableArray.CreateBuilder<MapWaypointLink>();
        links.Add(new MapWaypointLink(1, 2));
        links.Add(new MapWaypointLink(2, 3));
        links.Add(new MapWaypointLink(3, 4));
        links.Add(new MapWaypointLink(5, 6));
        links.Add(new MapWaypointLink(6, 7));
        links.Add(new MapWaypointLink(7, 8));
        links.Add(new MapWaypointLink(8, 5));
        links.Add(new MapWaypointLink(9, 10));
        return TerrainMap.Build(map with { WaypointLinks = links.ToImmutable() }, "structures");
    }

    private sealed class StructuresWorld
    {
        public StructuresWorld(bool lake = false)
        {
            Fixture = new SyntheticFixture(TestRate.Default, files: StructuresFiles());
            Terrain = StructuresTerrain(lake);
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, StructuresPlayers, SyntheticFixture.FixtureId, Fixture.Fingerprint, Terrain.Fingerprint), Fixture.Templates, Terrain);
            Feeder = new Feeder(World);
        }

        private StructuresWorld(StructuresWorld source)
        {
            Fixture = source.Fixture;
            Terrain = source.Terrain;
            World = WorldSnapshot.Restore(WorldSnapshot.Write(source.World), Fixture.Templates, Fixture.Fingerprint, Terrain);
            Feeder = new Feeder(World);
        }

        /// <summary>A world restored from a snapshot of <paramref name="source"/> (same fixture and terrain).</summary>
        public static StructuresWorld RestoreOf(StructuresWorld source) => new(source);

        public SyntheticFixture Fixture { get; }
        public TerrainMap Terrain { get; }
        public SimulationWorld World { get; }
        public Feeder Feeder { get; }

        public Entity Spawn(string template, int owner, int x, int y, decimal angle = 0m)
        {
            Check.That(Fixture.Templates.TryGet(template, out var info) && info.IsInstantiable, $"{template} is instantiable ({(Fixture.Templates.TryGet(template, out var missing) ? string.Join(",", missing.MissingModules) : "unknown")}).");
            var id = World.RequestSpawn(info.Index, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.FromDecimal(angle));
            World.Step();
            return World.FindEntity(id)!;
        }

        public void Run(int frames) // original frames (1/30 s of game time)
        {
            for (var tick = 0; tick < World.Frames(frames); tick++) World.Step();
        }

        public Entity[] Of(string template) => World.Entities.Where(entity => entity.Template.Name == template).OrderBy(entity => entity.Id.Value).ToArray();

        public IEnumerable<Entity> Alive(string template) => Of(template).Where(entity => !entity.EffectivelyDead && !entity.DestroyRequested);

        public static T Module<T>(Entity entity) where T : SimModule => entity.Modules.OfType<T>().Single();
    }

    private static void Damage(StructuresWorld sw, Entity target, decimal amount, int type = DamageTypes.Explosion, int death = DeathTypes.Normal, EntityId? source = null)
        => sw.World.Damage(target.Id, new DamageInfo(source ?? EntityId.Invalid, Fixed.FromDecimal(amount), type, death, false));

    public static void StructuresFixtureInstantiates()
    {
        var sw = new StructuresWorld();
        var missing = sw.Fixture.Templates.Templates.Where(template => template.Name.StartsWith("Sw", StringComparison.Ordinal) && !template.IsInstantiable).Select(template => template.Name + ": " + string.Join(",", template.MissingModules)).ToArray();
        Check.That(missing.Length == 1 && missing[0].StartsWith("SwBonedOcl", StringComparison.Ordinal), "Every fixture object instantiates except the one whose BoneFXUpdate would create objects at bones: " + string.Join("; ", missing));
        Check.That(missing[0].Contains("BoneFXUpdate", StringComparison.Ordinal), "The refused module is named: " + missing[0]);
    }

    public static void StructuresUpgradeDieFreesTheProducersUpgrade()
    {
        var sw = new StructuresWorld();
        var carrier = sw.Spawn("SwDroneCarrier", 1, 100, 100);
        sw.World.CompleteUpgrade(new PlayerId(1), carrier.Id, "Upgrade_SwDrone");
        sw.Run(3);
        var drone = sw.Alive("SwDrone").Single();
        Check.That(UpgradeBit(sw, carrier), "The carrier holds its drone upgrade.");
        Check.That(Module<UpgradeDie>(drone).Producer == carrier.Id, "The creation list recorded the carrier as the drone's producer.");

        // A second world state: the producer survives a snapshot and the drone's death still frees the upgrade afterwards.
        var restored = StructuresWorld.RestoreOf(sw);
        var restoredDrone = restored.Alive("SwDrone").Single();
        Check.That(Module<UpgradeDie>(restoredDrone).Producer == carrier.Id, "The producer id survives a save and restore.");

        sw.World.Kill(drone.Id);
        sw.Run(2);
        Check.That(!UpgradeBit(sw, carrier), "The drone's death removed the upgrade from its producer.");
        restored.World.Kill(restoredDrone.Id);
        restored.Run(2);
        Check.That(!UpgradeBit(restored, restored.World.FindEntity(carrier.Id)!), "The restored world frees the upgrade just the same.");
        Check.That(sw.World.StateHash == restored.World.StateHash, "Original and restored worlds hash alike after the same events.");
    }

    /// <summary>Fixed-point health within a hundredth of an expected value (percentages are not exact in 16 fractional bits).</summary>
    private static bool Near(Fixed actual, decimal expected) => (actual - Fixed.FromDecimal(expected)).Abs() < Fixed.FromDecimal(0.01m);

    private static bool UpgradeBit(StructuresWorld sw, Entity carrier)
        => sw.Fixture.Templates.Upgrades.TryGet("Upgrade_SwDrone", out var upgrade) && carrier.CompletedUpgrades.Has(upgrade.Index);

    private static T Module<T>(Entity entity) where T : SimModule => StructuresWorld.Module<T>(entity);

    public static void StructuresCollapseRunsItsPhases()
    {
        var sw = new StructuresWorld();
        var building = sw.Spawn("SwBuilding", 0, 300, 200);
        var collapse = Module<StructureCollapseUpdate>(building);
        var events = new List<SimEvent>();
        sw.World.Kill(building.Id);
        for (var tick = 0; tick < sw.World.Frames(200) && !collapse.IsCollapsed; tick++) events.AddRange(sw.World.Step().Events);
        Check.That(collapse.IsCollapsed, "The destroyed building finished collapsing.");
        Check.That(events.Any(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Test"), "The INITIAL effect list was requested.");
        Check.That(events.Any(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Other"), "The FINAL effect list was requested.");
        var debris = sw.Of("SwDebris").Length;
        Check.That(debris >= 2, $"The burst and the final creation lists produced debris ({debris}).");
        Check.That(collapse.VisualHeight + Fixed.FromInt(20) <= Fixed.Zero, "The visual height sank below the whole template height.");
        Check.That(sw.World.FindEntity(building.Id) is not null, "The corpse stays (KeepObjectDie).");

        // Two independent runs agree, and a save in the middle of the collapse continues identically.
        var a = new StructuresWorld();
        var b = new StructuresWorld();
        var buildingA = a.Spawn("SwBuilding", 0, 300, 200);
        var buildingB = b.Spawn("SwBuilding", 0, 300, 200);
        a.World.Kill(buildingA.Id);
        b.World.Kill(buildingB.Id);
        a.Run(12);
        b.Run(12);
        var restored = StructuresWorld.RestoreOf(a);
        for (var tick = 0; tick < a.World.Frames(80); tick++)
        {
            a.World.Step();
            b.World.Step();
            restored.World.Step();
        }
        Check.That(a.World.StateHash == b.World.StateHash && a.World.StateHash == restored.World.StateHash, "Two runs and a save/restore in mid-collapse end with the same hash.");
    }

    public static void StructuresBurningTreesSpreadAndHurtWhoTouchesThem()
    {
        var sw = new StructuresWorld();
        var near = sw.Spawn("SwTree", 0, 200, 300);
        var far = sw.Spawn("SwTree", 0, 400, 300);
        var next = sw.Spawn("SwTree", 0, 225, 300);
        var walker = sw.Spawn("SwWalker", 1, 200, 304);
        // A tree that is not burning does not hurt a walker that touches it.
        sw.World.ResolveFootprintCollisions(walker, Fixed.Zero, Fixed.Zero);
        Check.That(walker.Body!.Health == Fixed.FromInt(100), "A tree that is not burning hurts nobody.");

        Damage(sw, near, 10, DamageTypes.Flame, DeathTypes.Burned);
        Check.That(near.HasStatus(ObjectStatusBits.Index("AFLAME")), "Flame damage set the tree alight.");
        sw.World.ResolveFootprintCollisions(walker, Fixed.Zero, Fixed.Zero);
        Check.That(walker.Body.Health == Fixed.FromInt(93), $"The burning tree's collide weapon hurt the walker touching it ({walker.Body.Health}).");

        sw.Run(40);
        Check.That(next.HasStatus(ObjectStatusBits.Index("AFLAME")), "The fire jumped to the tree 25 units away.");
        Check.That(!far.HasStatus(ObjectStatusBits.Index("AFLAME")), "The tree out of range stayed unburnt.");
        Check.That(sw.Of("SwDebris").Length > 0, "Embers were created by the burning trees.");
    }

    public static void StructuresDeployStyleUnitsDeployToShootAndPackToMove()
    {
        var sw = new StructuresWorld();
        // The deployer's AI acquires by itself (AutoAcquireEnemiesWhenIdle = Yes) and would deploy as soon as it sees the target; this test drives the gate with commands.
        sw.World.SelfAcquisition = false;
        var deployer = sw.Spawn("SwDeployer", 1, 100, 100);
        var target = sw.Spawn("SwTarget", 2, 140, 100);
        sw.Run(4);
        var ai = Module<DeployStyleAIUpdate>(deployer);
        var deployedBit = ObjectStatusBits.Index("DEPLOYED");
        Check.That(ai.IsPacked && !deployer.HasStatus(deployedBit), "A fresh unit is packed.");

        // The gate lives in the execution of an accepted fire command (so that a replay, which holds only accepted commands, repeats the deployment).
        var health = target.Body!.Health;
        var refusals = sw.World.RejectionCounts[(int)CommandRejection.WeaponNotReady];
        Check.That(sw.Feeder.FireWeapon(1, deployer.Id.Value, target.Id.Value).Accepted, "The fire order is accepted.");
        sw.Run(2);
        Check.That(target.Body.Health == health && !ai.IsDeployed && !ai.IsPacked, "It only starts the deployment: no shot, the unit is no longer packed and not yet deployed.");
        Check.That(sw.World.RejectionCounts[(int)CommandRejection.WeaponNotReady] == refusals + 1, "The dropped shot is counted, not lost silently.");
        sw.Run(20);
        Check.That(!ai.IsDeployed, "Twenty ticks into the unpack time of thirty the unit is not deployed yet.");
        sw.Feeder.FireWeapon(1, deployer.Id.Value, target.Id.Value);
        sw.Run(2);
        Check.That(target.Body.Health == health, "Still no shot while unpacking.");
        sw.Run(12);
        Check.That(ai.IsDeployed && deployer.HasStatus(deployedBit), "After the unpack time the unit is deployed and carries the DEPLOYED status.");
        sw.Feeder.FireWeapon(1, deployer.Id.Value, target.Id.Value);
        sw.Run(2);
        Check.That(target.Body.Health < health, "A deployed unit fires and the shot hits.");

        // Moving: the unit packs first (fifteen ticks), then drives.
        var startX = deployer.X;
        Check.That(sw.Feeder.SetMoveTarget(1, deployer.Id.Value, 100, 180).Accepted, "The move order is accepted.");
        sw.Run(8);
        Check.That(!deployer.HasStatus(deployedBit) && deployer.X == startX && deployer.Y == Fixed.FromInt(100), "While packing the unit stands still and is no longer deployed.");
        sw.Run(25);
        Check.That(ai.IsPacked && deployer.Y > Fixed.FromInt(100), "Packed, it drives off.");
        var damaged = target.Body.Health;
        sw.Feeder.FireWeapon(1, deployer.Id.Value, target.Id.Value);
        sw.Run(2);
        Check.That(target.Body.Health == damaged || ai.IsDeployed, "A unit that is not deployed does not shoot.");

        // Save and restore while deployed continues identically.
        var other = new StructuresWorld();
        var deployer2 = other.Spawn("SwDeployer", 1, 100, 100);
        other.Spawn("SwTarget", 2, 140, 100);
        other.Run(4);
        other.Feeder.FireWeapon(1, deployer2.Id.Value, other.Of("SwTarget")[0].Id.Value);
        other.Run(12);
        var restored = StructuresWorld.RestoreOf(other);
        other.Run(60);
        restored.Run(60);
        Check.That(Module<DeployStyleAIUpdate>(restored.World.FindEntity(deployer2.Id)!).IsDeployed && other.World.StateHash == restored.World.StateHash, "A restored deploying unit finishes deploying and stays hash-identical.");
    }

    public static void StructuresTrainsRunTheirTrackAndHaulCarriages()
    {
        var sw = new StructuresWorld();
        var loco = sw.Spawn("SwLoco", 0, 50, 100);
        var rail = Module<RailroadBehavior>(loco);
        Check.That(rail.Conductor == RailroadBehavior.ConductorStates.Accelerate, "The locomotive starts accelerating.");
        sw.Run(3);
        var cars = sw.Of("SwCar");
        Check.That(cars.Length == 2, $"Two carriages were created behind the locomotive ({cars.Length}).");
        Check.That(rail.Trailer == cars[0].Id, "The locomotive pulls the first carriage.");

        // The locomotive and its cars are in the wings until the conductor has covered two major radii, then they roll onto the track.
        Check.That(rail.IsWaitingInWings, "Before it has moved two radii the locomotive is still in the wings.");
        var maxSpeed = Fixed.Zero;
        var reachedStation = false;
        var leftStation = false;
        var spacingChecked = false;
        for (var tick = 0; tick < sw.World.Frames(700) && !spacingChecked; tick++)
        {
            sw.World.Step();
            maxSpeed = Fixed.Max(maxSpeed, rail.Speed);
            if (rail.Conductor == RailroadBehavior.ConductorStates.WaitAtStation) reachedStation = true;
            if (reachedStation && rail.Conductor == RailroadBehavior.ConductorStates.Accelerate) leftStation = true;
            if (leftStation && loco.X > Fixed.FromInt(450))
            {
                spacingChecked = true;
                // Cars hang one hitch step (two major radii = 30) behind each other.
                Check.That((loco.X - cars[0].X - Fixed.FromInt(30)).Abs() < Fixed.One && (cars[0].X - cars[1].X - Fixed.FromInt(30)).Abs() < Fixed.One,
                    $"The carriages trail the locomotive at the hitch spacing ({loco.X} / {cars[0].X} / {cars[1].X}).");
                Check.That((loco.Y - Fixed.FromInt(100)).Abs() < Fixed.One && (cars[1].Y - Fixed.FromInt(100)).Abs() < Fixed.One, "The train runs along its track.");
            }
        }
        Check.That(reachedStation, "The train braked and waited at the station waypoint.");
        Check.That(leftStation, "After the waiting time the train set off again.");
        Check.That(spacingChecked, "The train ran on past the station.");
        Check.That(maxSpeed <= sw.World.PerFrame(Fixed.FromInt(3)), $"The speed never exceeded SpeedMax ({maxSpeed}).");
        Check.That(maxSpeed > sw.World.PerFrame(Fixed.FromInt(2)), "The locomotive reached (nearly) its top speed.");

        // The end of an open track: the whole train leaves it and is destroyed, one carriage after the other.
        sw.Run(400);
        Check.That(sw.Alive("SwLoco").Count() + sw.Alive("SwCar").Count() == 0, "The train left the end of the track and was destroyed.");
        Check.That(sw.World.FindEntity(cars[1].Id) is null && sw.World.FindEntity(loco.Id) is null, "Nothing of the train is left in the world.");
    }

    public static void StructuresTrainsKillWhatIsOnTheTrack()
    {
        var sw = new StructuresWorld();
        var plant = sw.Spawn("SwPlant", 1, 170, 100);
        var trooper = sw.Spawn("SwTrooper", 2, 120, 100);
        var heavy = sw.Spawn("SwHeavy", 2, 140, 104);
        var bystander = sw.Spawn("SwHeavy", 2, 140, 160);
        var bare = sw.Spawn("SwWalker", 2, 100, 100);
        var loco = sw.Spawn("SwLoco", 0, 50, 100);
        sw.Run(260);
        Check.That(trooper.EffectivelyDead, "A moving locomotive killed the infantry on its track.");
        Check.That(heavy.EffectivelyDead, "It killed the vehicle beside the rails, whose box it overlaps.");
        Check.That(plant.EffectivelyDead || plant.DestroyRequested, "A power plant on the track is destroyed.");
        Check.That(!bystander.EffectivelyDead, "Something 60 units off the track is left alone.");
        Check.That(!bare.EffectivelyDead, "An object without physics is ignored (the reference returns when the victim has no PhysicsBehavior).");
        Check.That(!loco.EffectivelyDead, "The locomotive itself survives.");

        // Two locomotives that meet destroy each other.
        var second = new StructuresWorld();
        var first = second.Spawn("SwLoco", 0, 50, 100);
        var other = second.Spawn("SwLoco", 0, 50, 100);
        second.Run(120);
        Check.That(first.EffectivelyDead && other.EffectivelyDead, "Two trains in the same place kill each other.");
    }

    public static void StructuresDemoTrapsGoOffForEnemiesOnly()
    {
        var sw = new StructuresWorld();
        var trap = sw.Spawn("SwTrap", 1, 300, 200);
        var friend = sw.Spawn("SwWalker", 3, 320, 200);
        sw.Run(5);
        Check.That(!Module<DemoTrapUpdate>(trap).Detonated && !trap.EffectivelyDead, "An ally (also an ally of the trap's owner) in range does not set it off.");
        var enemy = sw.Spawn("SwWalker", 2, 330, 200);
        var before = enemy.Body!.Health;
        sw.Run(3);
        Check.That(Module<DemoTrapUpdate>(trap).Detonated && trap.EffectivelyDead, "An enemy in range sets the trap off and it kills itself.");
        Check.That(enemy.Body.Health < before, "The detonation weapon hurt the enemy.");
        Check.That(friend.Body!.Health == Fixed.FromInt(100), "The weapon spares allies.");

        var manual = new StructuresWorld();
        var manualTrap = manual.Spawn("SwTrap", 1, 300, 200);
        Module<DemoTrapUpdate>(manualTrap).SetMode(manual.World, DemoTrapUpdate.DemoTrapMode.Manual);
        var victim = manual.Spawn("SwWalker", 2, 310, 200);
        manual.Run(5);
        Check.That(!manualTrap.EffectivelyDead, "A trap in manual mode never scans.");
        Module<DemoTrapUpdate>(manualTrap).SetMode(manual.World, DemoTrapUpdate.DemoTrapMode.Detonate);
        manual.Run(3);
        Check.That(manualTrap.EffectivelyDead && victim.Body!.Health < Fixed.FromInt(100), "Told to detonate, it goes off.");

        var killed = new StructuresWorld();
        var doomed = killed.Spawn("SwTrap", 1, 300, 200);
        var nearby = killed.Spawn("SwWalker", 2, 320, 200);
        Module<DemoTrapUpdate>(doomed).SetMode(killed.World, DemoTrapUpdate.DemoTrapMode.Manual);
        killed.Run(2);
        killed.World.Damage(doomed.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(500), DamageTypes.Explosion, DeathTypes.Normal, false));
        killed.Run(3);
        Check.That(Module<DemoTrapUpdate>(doomed).Detonated && nearby.Body!.Health < Fixed.FromInt(100), "A trap that is destroyed detonates (DetonateWhenKilled).");
    }

    public static void StructuresMinesDetonateOnceAndRegenerate()
    {
        var sw = new StructuresWorld();
        var mine = sw.Spawn("SwMine", 1, 300, 200);
        var module = Module<MinefieldBehavior>(mine);
        var ally = sw.Spawn("SwWalker", 3, 300, 200);
        sw.World.ResolveFootprintCollisions(ally, Fixed.Zero, Fixed.Zero);
        Check.That(module.VirtualMinesRemaining == 4, "An ally does not set the mine off (DetonatedBy = ENEMIES NEUTRAL).");
        var enemy = sw.Spawn("SwWalker", 2, 306, 200);
        var health = enemy.Body!.Health;
        sw.World.ResolveFootprintCollisions(enemy, Fixed.Zero, Fixed.Zero);
        Check.That(module.VirtualMinesRemaining == 3, "An enemy sets off one virtual mine.");
        Check.That(enemy.Body.Health < health, "The mine's weapon hurt it.");
        Check.That(mine.Body!.Health == Fixed.FromInt(75), $"The mine's health fell to the share of its remaining virtual mines ({mine.Body.Health}).");
        sw.World.ResolveFootprintCollisions(enemy, Fixed.Zero, Fixed.Zero);
        Check.That(module.VirtualMinesRemaining == 3, "The same unit must move before it sets off the next one.");
        // Walking across the mine sets off more, one per five units of progress.
        Check.That(sw.Feeder.SetMoveTarget(2, enemy.Id.Value, 280, 200).Accepted, "The enemy is ordered across the mine.");
        sw.Run(45);
        Check.That(module.VirtualMinesRemaining < 3, $"Walking across the mine set off further virtual mines ({module.VirtualMinesRemaining} left).");

        // Regeneration: AutoHealBehavior heals 25 per second, which restores a virtual mine each time.
        var spent = module.VirtualMinesRemaining;
        sw.Run(100);
        Check.That(module.VirtualMinesRemaining > spent, $"The mine regenerates virtual mines through healing ({spent} -> {module.VirtualMinesRemaining}).");

        // Damage in step with the count: a hit that takes a quarter of its health detonates one mine.
        var count = module.VirtualMinesRemaining;
        Damage(sw, mine, 25, DamageTypes.Explosion);
        Check.That(module.VirtualMinesRemaining == count - 1, "A hit that takes a quarter of the health sets one virtual mine off.");

        // Mines whose creator is gone stop healing and degenerate until destroyed.
        var layerWorld = new StructuresWorld();
        var layer = layerWorld.Spawn("SwMineLayer", 1, 100, 100);
        layerWorld.World.CompleteUpgrade(new PlayerId(1), layer.Id, "Upgrade_SwDrone");
        layerWorld.Run(3);
        var laid = layerWorld.Alive("SwMine").Single();
        layerWorld.World.Kill(layer.Id);
        layerWorld.Run(30 * 3);
        Check.That(Module<MinefieldBehavior>(laid).IsDraining, "With its creator dead the mine degenerates.");
        layerWorld.Run(30 * 8);
        Check.That(laid.DestroyRequested || layerWorld.World.FindEntity(laid.Id) is null || laid.EffectivelyDead, "The degenerating mine is destroyed.");
    }

    public static void StructuresBridgesTowersAndScaffolds()
    {
        var sw = new StructuresWorld();
        var bridge = sw.Spawn("SwBridge", 0, 300, 200);
        var towerA = sw.Spawn("SwBridgeTower", 0, 260, 190);
        var towerB = sw.Spawn("SwBridgeTower", 0, 340, 190);
        var link = Module<BridgeBehavior>(bridge);
        link.SetTower(0, towerA);
        link.SetTower(1, towerB);
        Module<BridgeTowerBehavior>(towerA).SetBridge(bridge);
        Module<BridgeTowerBehavior>(towerB).SetBridge(bridge);
        var onDeck = sw.Spawn("SwWalker", 1, 300, 200);
        var heavy = sw.Spawn("SwHeavy", 1, 310, 200);
        var beside = sw.Spawn("SwWalker", 1, 300, 240);

        Damage(sw, bridge, 20);
        Check.That(Near(towerA.Body!.Health, 90m) && Near(towerB.Body!.Health, 90m), $"Ten percent damage to the bridge is ten percent of each tower ({towerA.Body!.Health}/{towerB.Body!.Health})."); 
        Damage(sw, towerA, 10);
        Check.That(Near(towerB.Body!.Health, 80m) && Near(bridge.Body!.Health, 160m), $"Damage to a tower goes to the other tower and the bridge by its share ({towerB.Body!.Health}/{bridge.Body!.Health}).");
        sw.World.Damage(bridge.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(20), DamageTypes.Healing, DeathTypes.None, false));
        Check.That(Near(towerA.Body!.Health, 90m), $"Healing the bridge heals the towers ({towerA.Body!.Health}).");

        var debris = sw.Of("SwDebris").Length;
        sw.World.Kill(bridge.Id);
        sw.Run(3);
        Check.That(towerA.EffectivelyDead && towerB.EffectivelyDead, "A destroyed bridge kills its towers.");
        Check.That(onDeck.EffectivelyDead, "Whatever stands on the deck without physics dies.");
        Check.That(!heavy.EffectivelyDead && !beside.EffectivelyDead, "An object with physics and one beside the deck are left alone.");
        Check.That(sw.Of("SwDebris").Length == debris + 1, "The delay-0 creation list went off at once.");
        sw.Run(40);
        Check.That(sw.Of("SwDebris").Length == debris + 2, "The creation list with a one second delay went off after it, exactly once.");
        sw.Run(40);
        Check.That(sw.Of("SwDebris").Length == debris + 2, "And never again.");

        // A tower's death kills the bridge.
        var second = new StructuresWorld();
        var bridge2 = second.Spawn("SwBridge", 0, 300, 200);
        var tower2 = second.Spawn("SwBridgeTower", 0, 260, 190);
        Module<BridgeBehavior>(bridge2).SetTower(0, tower2);
        Module<BridgeTowerBehavior>(tower2).SetBridge(bridge2);
        second.World.Kill(tower2.Id);
        second.Run(2);
        Check.That(bridge2.EffectivelyDead, "The death of a tower kills the bridge.");

        // The scaffold rises, builds across, and the reversed motion tears down and sinks until it is gone.
        var scaffoldWorld = new StructuresWorld();
        var scaffold = scaffoldWorld.Spawn("SwScaffold", 0, 100, 100);
        var module = Module<BridgeScaffoldBehavior>(scaffold);
        module.SetPositions((Fixed.FromInt(100), Fixed.FromInt(100), Fixed.Zero), (Fixed.FromInt(100), Fixed.FromInt(100), Fixed.FromInt(30)), (Fixed.FromInt(160), Fixed.FromInt(100), Fixed.FromInt(30)));
        module.SetSpeeds(scaffoldWorld.World.PerFrame(Fixed.FromInt(4)), scaffoldWorld.World.PerFrame(Fixed.FromInt(3)));
        module.SetMotion(scaffoldWorld.World, BridgeScaffoldBehavior.Motion.Rise);
        scaffoldWorld.Run(10);
        Check.That(scaffold.Z > Fixed.Zero && scaffold.Z < Fixed.FromInt(30) && module.CurrentMotion == BridgeScaffoldBehavior.Motion.Rise, "The scaffold is rising.");
        scaffoldWorld.Run(150);
        Check.That(module.CurrentMotion == BridgeScaffoldBehavior.Motion.Still && scaffold.X == Fixed.FromInt(160) && scaffold.Z == Fixed.FromInt(30), $"It rose, built across and came to rest ({scaffold.X},{scaffold.Z}).");
        module.ReverseMotion(scaffoldWorld.World);
        Check.That(module.CurrentMotion == BridgeScaffoldBehavior.Motion.TearDownAcross, "Reversing a resting scaffold tears it down.");
        scaffoldWorld.Run(300);
        Check.That(scaffoldWorld.World.FindEntity(scaffold.Id) is null, "Torn down and sunk, the scaffold is gone.");
    }

    public static void StructuresWorldEffects()
    {
        // OCLUpdate: creates its list after the delay, repeatedly, not at once.
        var drop = new StructuresWorld();
        drop.Spawn("SwDropZone", 1, 300, 200);
        drop.Run(10);
        Check.That(drop.Of("SwDebris").Length == 0, "The drop zone does not create on its first try.");
        drop.Run(40);
        Check.That(drop.Of("SwDebris").Length == 1, $"After the delay it created its list once ({drop.Of("SwDebris").Length}).");
        drop.Run(40);
        Check.That(drop.Of("SwDebris").Length == 2, "And again after the next delay.");

        // A faction-triggered pad does nothing for the neutral player and runs its faction's list after a playable owner takes it.
        var pad = new StructuresWorld();
        var neutralPad = pad.Spawn("SwPad", 0, 300, 200);
        pad.Run(100);
        Check.That(pad.Of("SwDebris").Length == 0, "A neutral pad is idle.");
        Check.That(pad.World.TransferOwnership(neutralPad.Id, new PlayerId(1)), "The pad is captured by a playable side.");
        pad.Run(100);
        Check.That(pad.Of("SwDebris").Length >= 1, "The captured pad runs the list of its owner's side.");
        var beta = new StructuresWorld();
        var betaPad = beta.Spawn("SwPad", 2, 300, 200);
        beta.Run(100);
        Check.That(beta.Of("SwDebris").Length == 0, "A pad whose owner's side has no FactionOCL creates nothing.");

        // DynamicShroudClearingRangeUpdate: the range grows from nothing to the native range, holds, and shrinks to the final vision.
        var shroud = new StructuresWorld();
        var reveal = shroud.Spawn("SwReveal", 1, 300, 200);
        var provider = Module<DynamicShroudClearingRangeUpdate>(reveal);
        Check.That(!provider.TryGetShroudRange(out _) || provider.TryGetShroudRange(out var start) && start <= Fixed.FromInt(10), "The range starts at nothing (or is not applied yet).");
        shroud.Run(45);
        Check.That(provider.TryGetShroudRange(out var middle) && middle > Fixed.FromInt(95) && middle <= Fixed.FromInt(100), $"At the sustain phase the range is the native 100 ({middle}).");
        Check.That(shroud.World.IsPositionVisibleTo(new PlayerId(1), Fixed.FromInt(300 + 60), Fixed.FromInt(200)), "At full range the point 60 away is visible.");
        shroud.Run(30);
        Check.That(provider.TryGetShroudRange(out var shrinking) && shrinking < Fixed.FromInt(100) && shrinking > Fixed.FromInt(20), $"Then it shrinks ({shrinking}).");
        shroud.Run(60);
        Check.That(provider.TryGetShroudRange(out var final) && final == Fixed.FromInt(20), $"It ends at the final vision ({final}).");
        Check.That(shroud.World.Vision is not null && !shroud.World.IsPositionVisibleTo(new PlayerId(1), Fixed.FromInt(300 + 60), Fixed.FromInt(200)), "The vision map follows the range: a point 60 away is no longer visible.");

        // FloatUpdate: an enabled object sits on the water surface.
        var lake = new StructuresWorld(lake: true);
        var buoy = lake.Spawn("SwBuoy", 0, 50, 50);
        lake.Run(3);
        Check.That(buoy.Z == Fixed.FromInt(5), $"The floating object is on the water level ({buoy.Z}).");
        var dry = lake.Spawn("SwBuoy", 0, 300, 300);
        lake.Run(3);
        Check.That(dry.Z == Fixed.Zero, "Outside the water it stays on the ground.");

        // CheckpointUpdate and EnemyNearUpdate.
        var gate = new StructuresWorld();
        var checkpoint = gate.Spawn("SwCheckpoint", 1, 300, 200);
        var watcher = gate.Spawn("SwWatcher", 1, 100, 100);
        var gateModule = Module<CheckpointUpdate>(checkpoint);
        gate.Run(5);
        Check.That(!gateModule.IsOpen && gateModule.GateRadius == Fixed.FromInt(10), "Without anybody near the gate stays closed at its full size.");
        gate.Spawn("SwWalker", 3, 330, 200);
        gate.Run(40);
        Check.That(gateModule.IsOpen && gateModule.GateRadius < Fixed.FromInt(5), $"An ally arriving opens it and the gate shrinks ({gateModule.GateRadius}).");
        gate.Spawn("SwWalker", 2, 340, 210);
        gate.Run(40);
        Check.That(!gateModule.IsOpen && gateModule.GateRadius > Fixed.FromInt(5), "An enemy closes it again.");
        Check.That(!Module<EnemyNearUpdate>(watcher).IsEnemyNear, "Nobody hostile is near the watcher.");
        gate.Spawn("SwWalker", 2, 130, 100);
        gate.Run(120);
        Check.That(Module<EnemyNearUpdate>(watcher).IsEnemyNear, "An enemy within its vision range raises the alarm.");

        // Firestorm: the geometry grows and burns what it covers.
        var storm = new StructuresWorld();
        var victim = storm.Spawn("SwWalker", 2, 330, 200);
        var distant = storm.Spawn("SwWalker", 2, 500, 200);
        var fire = storm.Spawn("SwFirestorm", 1, 300, 200);
        var geometry = Module<FirestormDynamicGeometryInfoUpdate>(fire);
        storm.Run(60);
        Check.That(geometry.CurrentMajorRadius > Fixed.FromInt(5) && geometry.CurrentMajorRadius < Fixed.FromInt(60), $"The firestorm is growing ({geometry.CurrentMajorRadius}).");
        storm.Run(90);
        Check.That(victim.Body!.Health < Fixed.FromInt(100), "The walker 30 units away was burnt once the storm covered it.");
        Check.That(distant.Body!.Health == Fixed.FromInt(100), "The far one was not.");
        var peak = geometry.CurrentMajorRadius;
        storm.Run(60);
        Check.That(geometry.CurrentMajorRadius < peak || geometry.Finished, "After the transition time the storm shrinks back (ReverseAtTransitionTime).");
        storm.Run(100);
        Check.That(geometry.Finished, "And it finishes.");
    }

    public static void StructuresWeaponBonusAndCostModifier()
    {
        var sw = new StructuresWorld();
        var ally = sw.Spawn("SwShooter", 3, 330, 200);
        var enemy = sw.Spawn("SwShooter", 2, 330, 210);
        var far = sw.Spawn("SwShooter", 1, 500, 200);
        var beacon = sw.Spawn("SwBeacon", 1, 300, 200);
        sw.Run(3);
        Check.That(NameTablesPhase5.WeaponBonusCondition.TryIndexOf("FRENZY_ONE", out var frenzy), "FRENZY_ONE exists.");
        bool Has(Entity entity) => (entity.WeaponBonusConditions & (1u << frenzy)) != 0;
        Check.That(Has(ally) && !Has(enemy) && !Has(far), "An ally in range gets the frenzy bonus; an enemy and a far ally do not.");
        sw.Run(40);
        Check.That(!Has(ally) || Module<WeaponBonusUpdate>(beacon).ActiveCount > 0, "The bonus lasts its duration.");
        sw.Run(30);
        Check.That(!Has(ally), "After BonusDuration it is gone.");
        sw.Run(25);
        Check.That(Has(ally), "The next pulse gives it again.");

        // CostModifierUpgrade: a completed upgrade makes vehicles 10 percent cheaper for the owner; destroying the object takes it back.
        var costs = new StructuresWorld();
        var hall = costs.Spawn("SwDiscountHall", 1, 300, 200);
        var center = costs.Fixture.Templates.TryGet("AlphaSoldier", out var soldier) ? soldier : throw new InvalidOperationException("The skirmish fixture has no soldier.");
        Check.That(costs.World.ProductionCostOf(new PlayerId(1), soldier) == soldier.BuildCost, "Without the upgrade a unit costs its build cost.");
        var carrier = costs.Fixture.Templates.TryGet("SwDroneCarrier", out var vehicle) ? vehicle : throw new InvalidOperationException();
        var cost = vehicle.BuildCost;
        costs.World.CompleteUpgrade(new PlayerId(1), null, "Upgrade_SwCheap");
        costs.Run(2);
        Check.That(costs.World.ProductionCostOf(new PlayerId(1), vehicle) == Fixed.FromInt((cost * Fixed.FromDecimal(0.9m)).Floor()), "A vehicle costs 90 percent after the upgrade.");
        Check.That(costs.World.ProductionCostOf(new PlayerId(1), soldier) == soldier.BuildCost, "Other kinds are unaffected.");
        Check.That(costs.World.ProductionCostOf(new PlayerId(2), vehicle) == cost, "Other players are unaffected.");
        _ = carrier;
        var restored = StructuresWorld.RestoreOf(costs);
        Check.That(restored.World.ProductionCostOf(new PlayerId(1), vehicle) == Fixed.FromInt((cost * Fixed.FromDecimal(0.9m)).Floor()), "The modifier survives a save and restore.");
        Check.That(restored.World.StateHash == costs.World.StateHash, "The restored world hashes like the original.");
        costs.World.Kill(hall.Id);
        costs.Run(2);
        costs.World.RequestDestroy(hall.Id);
        costs.Run(2);
        Check.That(costs.World.ProductionCostOf(new PlayerId(1), vehicle) == cost, "Destroying the object takes the discount back.");
    }

    public static void StructuresDamMakesTheWaveFlood()
    {
        var sw = new StructuresWorld();
        var dam = sw.Spawn("SwDam", 0, 50, 50);
        var wave = sw.Spawn("SwWave", 0, 10, 10);
        var low = sw.Spawn("SwWalker", 1, 200, 250);
        var onWave = Module<WaveGuideUpdate>(wave);
        sw.Run(60);
        Check.That(onWave.IsDisabled && !onWave.IsMoving && wave.X == Fixed.FromInt(10), "The wave sits disabled until the dam breaks.");
        sw.World.Kill(dam.Id);
        sw.Run(5);
        Check.That(!onWave.IsDisabled, "The dam's death enabled the wave.");
        sw.Run(20);
        Check.That(!onWave.IsMoving, "The wave waits its WaveDelay before moving.");
        sw.Run(80);
        Check.That(onWave.IsMoving && wave.X >= Fixed.FromInt(100), $"Then it moves along its waypoint path ({wave.X}, {wave.Y}).");
        for (var tick = 0; tick < sw.World.Frames(200) && !low.EffectivelyDead; tick++) sw.World.Step();
        Check.That(low.EffectivelyDead && low.HasStatus(ObjectStatusBits.Index("WET")), "The wave flooded the object in its way.");
        sw.Run(200);
        Check.That(sw.World.FindEntity(wave.Id) is null || sw.World.FindEntity(wave.Id)!.DestroyRequested, "At the end of its path the wave is destroyed.");
    }

    public static void StructuresBoneFxModulesAreHonestShells()
    {
        var sw = new StructuresWorld();
        var boned = sw.Spawn("SwBoned", 1, 100, 100);
        sw.Run(3);
        Damage(sw, boned, 90);
        sw.Run(2);
        Check.That(boned.Body!.Health == Fixed.FromInt(10), "A building with the pair behaves like one without.");
        Check.That(!sw.Fixture.Templates.TryGet("SwBonedOcl", out var ocl) || !ocl.IsInstantiable, "A BoneFXUpdate that fills an OCL slot is not instantiable instead of silently losing the objects.");
    }

    public static void StructuresModulesSurviveSaveAndRestore()
    {
        var a = new StructuresWorld();
        var b = new StructuresWorld();
        foreach (var sw in new[] { a, b })
        {
            sw.Spawn("SwLoco", 0, 50, 100);
            sw.Spawn("SwBridge", 0, 300, 200);
            sw.Spawn("SwDropZone", 1, 400, 300);
            sw.Spawn("SwFirestorm", 1, 100, 300);
            sw.Spawn("SwReveal", 1, 500, 300);
            sw.Spawn("SwDam", 0, 20, 20);
            sw.Spawn("SwWave", 0, 10, 10);
            sw.Spawn("SwBeacon", 1, 300, 100);
            sw.Spawn("SwShooter", 1, 320, 100);
        }
        a.Run(150);
        b.Run(150);
        var restored = StructuresWorld.RestoreOf(a);
        Check.That(restored.World.StateHash == a.World.StateHash, "The restored world has the state hash of the saved one.");
        for (var tick = 0; tick < a.World.Frames(250); tick++)
        {
            a.World.Step();
            b.World.Step();
            restored.World.Step();
        }
        Check.That(a.World.StateHash == b.World.StateHash, "Two independent runs agree.");
        Check.That(a.World.StateHash == restored.World.StateHash, "The restored run continues identically (train, bridge, drop zone, firestorm, shroud, beacon).");
    }

    private static readonly string[] StructureGroupModules =
    [
        "UpgradeDie", "StructureCollapseUpdate", "FireWeaponCollide", "FireSpreadUpdate", "DeployStyleAIUpdate", "FloatUpdate", "RailroadBehavior", "DemoTrapUpdate", "MinefieldBehavior",
        "BridgeBehavior", "BridgeTowerBehavior", "BridgeScaffoldBehavior", "OCLUpdate", "DynamicShroudClearingRangeUpdate", "FireOCLAfterWeaponCooldownUpdate", "WaveGuideUpdate", "DamDie",
        "CheckpointUpdate", "EnemyNearUpdate", "FirestormDynamicGeometryInfoUpdate", "WeaponBonusUpdate", "CostModifierUpgrade", "BoneFXDamage", "BoneFXUpdate",
    ];

    /// <summary>
    /// Retail data: every shipped object that carries one of the structures-and-world modules is instantiable as far as these modules go; all of them (except the ones that need a map's waypoints
    /// or another object) are spawned side by side, run, killed and run on, twice, with identical tick hashes and a consistent incremental hash; every building with a collapse update finishes
    /// collapsing.
    /// </summary>
    public static void RealStructuresAndWorld(string directory)
    {
        var content = CnCNew.Content.ContentFileSystem.Open(directory);
        var options = CnCNew.Data.Compilation.DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = CnCNew.Data.Compilation.MapOverlayMode.Exclude };
        var graph = CnCNew.Data.Compilation.DefinitionCompiler.Compile(new RealSource(content), CnCNew.Data.Registry.ModuleRegistry.Default, options);
        var catalog = CnCNew.Simulation.Templates.TemplateCatalog.Build(graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var carriers = catalog.Templates.Where(template => template.Modules.Any(module => StructureGroupModules.Contains(module.Name)) || template.MissingModules.Any(module => StructureGroupModules.Contains(module))).ToArray();
        var refused = carriers.Where(template => template.MissingModules.Any(module => StructureGroupModules.Any(name => module.Contains(name, StringComparison.Ordinal)))).ToArray();
        Check.That(refused.Length == 0, "No retail object is refused because of a structures-and-world module: " + string.Join(", ", refused.Select(template => template.Name + " (" + string.Join(",", template.MissingModules) + ")")));
        var spawnable = carriers.Where(template => template.IsInstantiable && !template.Modules.Any(module => module.Name is "RailroadBehavior" or "WaveGuideUpdate" or "BridgeBehavior" or "BridgeTowerBehavior" or "BridgeScaffoldBehavior")).ToArray();
        var usage = StructureGroupModules.ToDictionary(name => name, name => catalog.Templates.Count(template => template.IsInstantiable && template.Modules.Any(module => module.Name == name)));
        Console.WriteLine($"       real structures: {carriers.Length} retail objects carry the group, {spawnable.Length} spawned; instantiable by module: " + string.Join(", ", usage.Where(pair => pair.Value > 0).Select(pair => $"{pair.Key} {pair.Value}")));
        var waiting = carriers.Where(template => !template.IsInstantiable).GroupBy(template => string.Join("+", template.MissingModules)).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        Console.WriteLine("       real structures: carriers still blocked by other modules: " + string.Join("; ", waiting.Select(group => $"{group.Key} x{group.Count()}")));
        foreach (var name in new[] { "RailroadBehavior", "StructureCollapseUpdate", "DeployStyleAIUpdate", "FireWeaponCollide", "BridgeBehavior" })
            Check.That(usage[name] > 0, $"Some retail object with {name} is instantiable.");

        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        (ulong[] Hashes, int Collapsed, int Collapsing) Run()
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 11, players, "real-structures", fingerprint), catalog);
            var sequence = 0UL;
            var column = 0;
            foreach (var template in spawnable)
            {
                var x = 500 + column % 25 * 300;
                var y = 500 + column / 25 * 300;
                column++;
                Check.That(world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, template.Name, new PlayerId(column % 2 + 1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template.Name} spawns.");
            }
            var hashes = new List<ulong>();
            for (var tick = 0; tick < world.Frames(120); tick++) hashes.Add(world.Step().StateHash);
            foreach (var entity in world.Entities.OrderBy(value => value.Id.Value).ToArray())
                world.Submit(new KillCommand(PlayerId.System, ++sequence, world.Tick, entity.Id));
            for (var tick = 0; tick < world.Frames(900); tick++) hashes.Add(world.Step().StateHash);
            world.VerifyIncrementalHash();
            var collapses = world.Entities.SelectMany(entity => entity.Modules.OfType<StructureCollapseUpdate>()).ToArray();
            return ([..hashes], collapses.Count(value => value.IsCollapsed), collapses.Count(value => value.IsCollapsing));
        }

        var (first, collapsed, collapsing) = Run();
        Check.That(collapsing == 0, $"Every collapsing building finished ({collapsed} collapsed, {collapsing} still going).");
        var (second, _, _) = Run();
        Check.That(first.SequenceEqual(second), "Two runs are hash identical on every tick.");

        // The retail train: the locomotive creates its two carriages from its own list and hauls them along a track of linked waypoints.
        var terrain = StructuresTerrain();
        TrainRun RunTrain()
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(2), TestRate.Default, 3, players, "real-train", fingerprint, terrain.Fingerprint), catalog, terrain);
            Check.That(world.Submit(new SpawnCommand(PlayerId.System, 1, world.Tick, "TrainEngine", PlayerId.System, Fixed.FromInt(60), Fixed.FromInt(100), Fixed.Zero, Fixed.Zero)).Accepted, "The retail locomotive spawns.");
            var hashes = new List<ulong>();
            var seen = 0;
            var braked = false;
            for (var tick = 0; tick < world.Frames(450); tick++)
            {
                hashes.Add(world.Step().StateHash);
                var cars = world.Entities.Count(entity => entity.Template.Name is "TrainCoal" or "TrainCab");
                seen = Math.Max(seen, cars);
                if (world.Entities.FirstOrDefault(entity => entity.Template.Name == "TrainEngine")?.Modules.OfType<RailroadBehavior>().FirstOrDefault() is { Conductor: RailroadBehavior.ConductorStates.WaitAtStation }) braked = true;
            }
            world.VerifyIncrementalHash();
            return new([..hashes], seen, braked, world.Entities.FirstOrDefault(entity => entity.Template.Name == "TrainEngine")?.X ?? Fixed.Zero);
        }
        var train = RunTrain();
        Check.That(train.Cars == 2, $"The retail locomotive made its two carriages ({train.Cars}).");
        Check.That(train.EngineX > Fixed.FromInt(300) || train.Braked, "The retail train rolls along the track.");
        Check.That(RunTrain().Hashes.SequenceEqual(train.Hashes), "Two retail train runs are hash identical on every tick.");
    }

    private readonly record struct TrainRun(ulong[] Hashes, int Cars, bool Braked, Fixed EngineX);
}

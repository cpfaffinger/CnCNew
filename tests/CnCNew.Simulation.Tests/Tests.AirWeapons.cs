using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>The weapon and pulse modules of the air group: EMP, leaflets, bunker buster, neutron blast, neutron missile, silo doors and the Spectre gunship.</summary>
static partial class Tests
{
    private static readonly ImmutableArray<PlayerConfiguration> AirPlayers = [new(new PlayerId(1), "Alpha", Team: 1), new(new PlayerId(2), "Beta", Team: 2), new(new PlayerId(3), "Gamma", Team: 1)];

    private static readonly IReadOnlyDictionary<string, string> AirWeaponFixtureFiles = new Dictionary<string, string>(AirFixtureFiles)
    {
        ["Data/INI/Object/AirWeaponFixture.ini"] = """
        Locomotor AirGroundLoco
          Surfaces = GROUND
          Speed = 60.0
          Acceleration = 300.0
          Braking = 300.0
          TurnRate = 360.0
        End
        Locomotor AirMissileLoco
          Surfaces = AIR
          Speed = 300.0
          Acceleration = 9000.0
          TurnRate = 360.0
        End
        SpecialPower AirNukePower
          Enum = SPECIAL_NEUTRON_MISSILE
          ReloadTime = 5000
        End
        SpecialPower AirGunshipPower
          Enum = SPECIAL_SPECTRE_GUNSHIP
          ReloadTime = 4000
        End

        Weapon AirTankGun
          PrimaryDamage = 5.0
          AttackRange = 500.0
          DelayBetweenShots = 100
        End
        Weapon AirBusterBombGun
          PrimaryDamage = 0.0
          DamageType = UNRESISTABLE
          AttackRange = 3000.0
          ProjectileObject = AirBusterBomb
          DelayBetweenShots = 1000
        End
        Weapon AirShockwave
          PrimaryDamage = 15.0
          PrimaryDamageRadius = 50.0
          AttackRange = 100.0
          RadiusDamageAffects = ALLIES ENEMIES NEUTRALS
        End
        Weapon AirOccupantWeapon
          PrimaryDamage = 0.0
          AttackRange = 10.0
        End
        Weapon AirNeutronWeapon
          PrimaryDamage = 0.0
          DamageType = UNRESISTABLE
          AttackRange = 5000.0
          ProjectileObject = AirNeutronMissile
          DelayBetweenShots = 100
        End
        Weapon AirGattlingGun
          PrimaryDamage = 10.0
          AttackRange = 2000.0
          DelayBetweenShots = 100
        End
        Weapon AirHowitzer
          PrimaryDamage = 40.0
          PrimaryDamageRadius = 30.0
          AttackRange = 2000.0
          RadiusDamageAffects = ALLIES ENEMIES NEUTRALS
          DelayBetweenShots = 300
        End

        Object AirEmpPulse
          Geometry = SPHERE
          GeometryMajorRadius = 10.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = EMPUpdate Emp01
            Lifetime = 900
            StartFadeTime = 300
            StartScale = 0.01
            TargetScaleMin = 3.0
            TargetScaleMax = 4.0
            DisabledDuration = 2000
            EffectRadius = 100.0
            DoesNotAffect = ALLIES
            DoesNotAffectMyOwnBuildings = Yes
            StartColor = R:32 G:64 B:255
            EndColor = R:0 G:0 B:0
            DisableFXParticleSystem = AirSparks
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirEmpTank
          Side = Beta
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL AirGroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AirTankGun
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirEmpInfantry
          Side = Beta
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Locomotor = SET_NORMAL AirGroundLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirEmpFactory
          Side = Beta
          KindOf = STRUCTURE FS_FACTORY
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirEmpWall
          Side = Beta
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirHardenedJet
          Side = Beta
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE EMP_HARDENED
          Locomotor = SET_NORMAL AirJetLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = JetAIUpdate Jet01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirLeafletBomb
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = LeafletDropBehavior Leaflet01
            Delay = 1000
            DisabledDuration = 1500
            AffectRadius = 100.0
            LeafletFXParticleSystem = AirSparks
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object AirBunker
          Side = Beta
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 4
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirRebel
          Side = Beta
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirBusterLauncher
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AirBusterBombGun
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirBusterBomb
          KindOf = PROJECTILE SMALL_MISSILE
          Locomotor = SET_NORMAL AirMissileLoco
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = MissileAIUpdate Missile01
            TryToFollowTarget = Yes
            FuelLifetime = 0
            IgnitionDelay = 0
            InitialVelocity = 0
            DistanceToTravelBeforeTurning = 0
            DistanceToTargetForLock = 0
            DetonateCallsKill = Yes
            KillSelfDelay = 200
          End
          Behavior = BunkerBusterBehavior Buster01
            UpgradeRequired = Upgrade_AirBusters
            DetonationFX = AirBustFx
            CrashThroughBunkerFX = AirCrashFx
            CrashThroughBunkerFXFrequency = 100
            SeismicEffectRadius = 200.0
            SeismicEffectMagnitude = 5.0
            ShockwaveWeaponTemplate = AirShockwave
            OccupantDamageWeaponTemplate = AirOccupantWeapon
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object AirNeutronShell
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = NeutronBlastBehavior Blast01
            BlastRadius = 60.0
            AffectAirborne = No
            AffectAllies = No
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirTruck
          Side = Beta
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Locomotor = SET_NORMAL AirGroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = TransportContain Transport01
            Slots = 4
            AllowInsideKindOf = INFANTRY
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirBike
          Side = Beta
          KindOf = SELECTABLE VEHICLE CLIFF_JUMPER
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirDrone
          Side = Beta
          KindOf = SELECTABLE VEHICLE DRONE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object AirSilo
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AirNeutronWeapon
          End
          Behavior = OCLSpecialPower Ocl01
            SpecialPowerTemplate = AirNukePower
            OCL = OCL_Wreck
          End
          Behavior = MissileLauncherBuildingUpdate Door01
            SpecialPowerTemplate = AirNukePower
            DoorOpenTime = 1000
            DoorWaitOpenTime = 500
            DoorCloseTime = 1000
            DoorOpeningFX = AirDoorFx
            DoorOpenFX = AirOpenFx
            DoorWaitingToCloseFX = AirLaunchFx
            DoorClosingFX = AirCrashFx
            DoorClosedFX = AirBustFx
            DoorOpenIdleAudio = AirHiss
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirNeutronMissile
          KindOf = PROJECTILE
          Geometry = SPHERE
          GeometryMajorRadius = 30.0
          GeometryIsSmall = Yes
          Body = ActiveBody Body01
            MaxHealth = 99999.0
            InitialHealth = 99999.0
          End
          Behavior = NeutronMissileUpdate Neutron01
            DistanceToTravelBeforeTurning = 100.0
            MaxTurnRate = 7200.0
            ForwardDamping = 0.1
            RelativeSpeed = 2.0
            TargetFromDirectlyAbove = 300.0
            LaunchFX = AirLaunchFx
            IgnitionFX = AirIgnitionFx
            SpecialSpeedTime = 1500
            SpecialSpeedHeight = 160.0
            SpecialAccelFactor = 1.0
            SpecialJitterDistance = 0.4
            DeliveryDecalRadius = 210.0
          End
          Behavior = HeightDieUpdate Height01
            TargetHeight = 100.0
            TargetHeightIncludesStructures = No
            OnlyWhenMovingDown = Yes
            SnapToGroundOnDeath = Yes
            InitialDelay = 1000
          End
          Behavior = NeutronMissileSlowDeathBehavior Slow01
            DestructionDelay = 500
            ScorchMarkSize = 320.0
            FXList = AirNukeFx
            Blast1Enabled = Yes
            Blast1Delay = 100
            Blast1ScorchDelay = 50
            Blast1InnerRadius = 40.0
            Blast1OuterRadius = 80.0
            Blast1MaxDamage = 100.0
            Blast1MinDamage = 10.0
            Blast1ToppleSpeed = 0.5
            Blast1PushForce = 10.0
            Blast2Enabled = Yes
            Blast2Delay = 200
            Blast2ScorchDelay = 100
            Blast2InnerRadius = 100.0
            Blast2OuterRadius = 120.0
            Blast2MaxDamage = 50.0
            Blast2MinDamage = 5.0
            Blast2ToppleSpeed = 0.4
            Blast2PushForce = 8.0
          End
        End

        Object AirGunship
          Side = Alpha
          KindOf = SELECTABLE VEHICLE AIRCRAFT CAN_ATTACK EMP_HARDENED
          Locomotor = SET_NORMAL AirGunshipLoco
          Locomotor = SET_PANIC AirTransitLoco
          Body = ActiveBody Body01
            MaxHealth = 600.0
            InitialHealth = 600.0
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = AirGunshipPower
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpectreGunshipUpdate Spectre01
            SpecialPowerTemplate = AirGunshipPower
            GattlingTemplateName = AirGattling
            HowitzerWeaponTemplate = AirHowitzer
            HowitzerFiringRate = 100
            HowitzerFollowLag = 100
            OrbitTime = 3000
            AttackAreaRadius = 80.0
            StrafingIncrement = 20.0
            OrbitInsertionSlope = 0.7
            RandomOffsetForHowitzer = 10.0
            TargetingReticleRadius = 30.0
            GunshipOrbitRadius = 100.0
            GattlingStrafeFXParticleSystem = AirSparks
            AttackAreaDecal
              Texture = None
              Style = SHADOW_ALPHA_DECAL
            End
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirGunshipCarrier
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = AirGunshipPower
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpectreGunshipDeploymentUpdate Deploy01
            SpecialPowerTemplate = AirGunshipPower
            GunshipTemplateName = AirGunship
            AttackAreaRadius = 80.0
            CreateLocation = CREATE_AT_EDGE_FARTHEST_FROM_TARGET
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirGattling
          Side = Alpha
          KindOf = PORTABLE_STRUCTURE CAN_ATTACK
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AirGattlingGun
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static SimulationWorld AirWeaponWorld(out SyntheticFixture fixture, out Feeder feeder, bool terrain = false, CnCNew.Simulation.Rules.RuleProfile? rules = null)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: AirWeaponFixtureFiles, rules: rules);
        var map = terrain ? CnCNew.Simulation.Terrain.TerrainMap.Build(SyntheticMaps.Build(200, 200, 1, (_, _) => 0), "airweapons") : null;
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 13, AirPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, map?.Fingerprint ?? string.Empty), fixture.Templates, map);
        feeder = new Feeder(world);
        return world;
    }

    private static Entity Named(SimulationWorld world, string template, int owner, int nth = 0)
        => world.Entities.Where(value => value.Template.Name == template && value.Owner.Value == owner).OrderBy(value => value.Id.Value).ElementAt(nth);

    /// <summary>
    /// An EMP pulse: nothing happens before <c>StartFadeTime</c>; then every enemy vehicle in the radius is disabled for <c>DisabledDuration</c> (it stands still and cannot fire, then carries on),
    /// airborne aircraft that are not EMP-hardened die, infantry, non-faction structures, allies (<c>DoesNotAffect = ALLIES</c>), own structures and far vehicles are untouched; a faction structure is disabled;
    /// the pulse object dies after its lifetime; the pulse survives a snapshot.
    /// </summary>
    public static void AirEmpPulse()
    {
        var world = AirWeaponWorld(out var fixture, out var feeder);
        Check.That(fixture.Templates.Templates.Where(value => value.Name.StartsWith("Air", StringComparison.Ordinal)).All(value => value.IsInstantiable),
            "Every air fixture object binds: " + string.Join(",", fixture.Templates.Templates.Where(value => value.Name.StartsWith("Air", StringComparison.Ordinal) && !value.IsInstantiable).Select(value => value.Name + ":" + string.Join("/", value.MissingModules))));
        feeder.Spawn(2, "AirEmpTank", owner: 2, x: 120, y: 100);
        feeder.Spawn(2, "AirEmpInfantry", owner: 2, x: 110, y: 110);
        feeder.Spawn(2, "AirEmpFactory", owner: 2, x: 100, y: 140);
        feeder.Spawn(2, "AirEmpWall", owner: 2, x: 140, y: 100);
        feeder.Spawn(3, "AirEmpTank", owner: 3, x: 80, y: 100);
        feeder.Spawn(1, "AirEmpFactory", owner: 1, x: 100, y: 60);
        feeder.Spawn(2, "AirEmpTank", owner: 2, x: 400, y: 100);
        feeder.Spawn(2, "AirTestJet", owner: 2, x: 100, y: 100);
        feeder.Spawn(2, "AirHardenedJet", owner: 2, x: 100, y: 105);
        feeder.Spawn(1, "AirEmpTank", owner: 1, x: 100, y: 300);
        for (var i = 0; i < world.Frames(80); i++) world.Step();
        var jet = Named(world, "AirTestJet", 2);
        var hardened = Named(world, "AirHardenedJet", 2);
        Check.That(jet.Z == Fixed.FromInt(60) && hardened.Z == Fixed.FromInt(60), "Both aircraft fly at their preferred height.");
        var victim = Named(world, "AirEmpTank", 2, 0);
        var far = Named(world, "AirEmpTank", 2, 1);
        var ally = Named(world, "AirEmpTank", 3);
        var attacker = Named(world, "AirEmpTank", 1);
        var ownFactory = Named(world, "AirEmpFactory", 1);
        var enemyFactory = Named(world, "AirEmpFactory", 2);

        feeder.Spawn(1, "AirEmpPulse", owner: 1, x: 100, y: 100);
        world.Step();
        var pulse = Get(world, "AirEmpPulse");
        var emp = pulse.Modules.OfType<EmpUpdate>().Single();
        var start = world.Tick;
        for (var i = 0; i < world.Frames(6); i++) world.Step();
        Check.That(!emp.HasPulsed && victim.EmpDisabledUntil == 0 && !jet.EffectivelyDead, "Nothing happens before StartFadeTime.");
        StepUntil(world, () => emp.HasPulsed, 20, "The pulse fires.");
        var pulseTick = world.Tick - 1;
        world.Step();
        Check.That(Math.Abs(pulseTick - (start + world.Frames(9))) <= 1, $"The pulse fires StartFadeTime (9 ticks) after its creation (created {start}, fired {pulseTick}).");
        Check.That(victim.EmpDisabledUntil >= pulseTick + world.Frames(59) && victim.EmpDisabledUntil <= pulseTick + world.Frames(62), $"An enemy vehicle in range is disabled for 60 ticks ({victim.EmpDisabledUntil} vs {pulseTick}).");
        Check.That(enemyFactory.EmpDisabledUntil > 0, "An enemy faction structure is disabled.");
        Check.That(jet.EffectivelyDead, "An airborne aircraft dies.");
        Check.That(!hardened.EffectivelyDead && hardened.EmpDisabledUntil == 0, "An EMP-hardened aircraft is spared.");
        Check.That(Named(world, "AirEmpInfantry", 2).EmpDisabledUntil == 0 && Named(world, "AirEmpWall", 2).EmpDisabledUntil == 0, "Infantry and a plain structure are not disabled.");
        Check.That(ally.EmpDisabledUntil == 0 && ownFactory.EmpDisabledUntil == 0 && far.EmpDisabledUntil == 0, "An ally, an own structure and a vehicle outside the radius are untouched.");

        // the disabled vehicle neither moves nor shoots; it recovers
        Check.That(feeder.SetMoveTarget(2, victim.Id.Value, 120, 300).Accepted, "It can be ordered.");
        var before = (victim.X, victim.Y);
        for (var i = 0; i < world.Frames(20); i++) world.Step();
        Check.That((victim.X, victim.Y) == before, "A disabled vehicle stands still.");
        Check.That(!feeder.FireWeapon(2, victim.Id.Value, attacker.Id.Value).Accepted, "A disabled vehicle cannot fire.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash, "A snapshot during the disable restores to the same hash.");
        for (var i = 0; i < restored.Frames(100); i++) Check.That(restored.Step().StateHash == world.Step().StateHash, "The restored world follows the original (step " + i + ").");
        Check.That(victim.Y > Fixed.FromInt(110), $"The vehicle drives on once the disable is over ({victim.Y}).");
        Check.That(!world.Entities.Any(value => value.Template.Name == "AirEmpPulse"), "The pulse object is gone after its lifetime.");
        Check.That(feeder.FireWeapon(2, victim.Id.Value, attacker.Id.Value).Accepted, "The recovered vehicle fires again.");
    }

    /// <summary>The leaflet bomb disables enemy infantry and vehicles around it when it dies; allies and structures are not affected.</summary>
    public static void AirLeafletBomb()
    {
        var world = AirWeaponWorld(out _, out var feeder);
        feeder.Spawn(1, "AirLeafletBomb", owner: 1, x: 100, y: 100);
        feeder.Spawn(2, "AirEmpInfantry", owner: 2, x: 130, y: 100);
        feeder.Spawn(2, "AirEmpTank", owner: 2, x: 100, y: 150);
        feeder.Spawn(2, "AirEmpWall", owner: 2, x: 100, y: 70);
        feeder.Spawn(3, "AirEmpInfantry", owner: 3, x: 70, y: 100);
        feeder.Spawn(2, "AirEmpTank", owner: 2, x: 500, y: 100);
        for (var i = 0; i < world.Frames(100); i++) world.Step();
        Check.That(Enumerable.All(world.Entities, value => value.EmpDisabledUntil == 0), "The first update puts the module to sleep: nothing is disabled while the bomb lives (the delay is not what triggers it).");
        world.Kill(Get(world, "AirLeafletBomb").Id);
        world.Step();
        var tick = world.Tick;
        Check.That(Named(world, "AirEmpInfantry", 2).EmpDisabledUntil == tick + world.Frames(46) - 1, $"Enemy infantry is disabled for 45 frames and through the last of them, from the frame the bomb died in ({Named(world, "AirEmpInfantry", 2).EmpDisabledUntil} vs {tick}).");
        Check.That(Named(world, "AirEmpTank", 2, 0).EmpDisabledUntil > 0 && Named(world, "AirEmpTank", 2, 1).EmpDisabledUntil == 0, "The enemy vehicle in range is disabled, the far one is not.");
        Check.That(Named(world, "AirEmpWall", 2).EmpDisabledUntil == 0 && Named(world, "AirEmpInfantry", 3).EmpDisabledUntil == 0, "A structure and an ally are not.");
    }

    private static (int Alive, int Fx, int Shock) BusterRun(bool upgraded, CnCNew.Simulation.Rules.RuleProfile? rules = null, bool shootDown = false)
    {
        var world = AirWeaponWorld(out _, out var feeder, rules: rules);
        feeder.Spawn(2, "AirBunker", owner: 2, x: 600, y: 100);
        feeder.Spawn(2, "AirRebel", owner: 2, x: 590, y: 100);
        feeder.Spawn(2, "AirRebel", owner: 2, x: 610, y: 100);
        feeder.Spawn(2, "AirEmpWall", owner: 2, x: 600, y: 130);
        feeder.Spawn(1, "AirBusterLauncher", owner: 1, x: 100, y: 100);
        world.Step();
        var bunker = Get(world, "AirBunker");
        foreach (var rebel in world.Entities.Where(value => value.Template.Name == "AirRebel").ToArray())
            Check.That(feeder.EnterContainer(2, rebel.Id.Value, bunker.Id.Value).Accepted, "A rebel enters the bunker.");
        world.Step();
        Check.That(bunker.Container!.ContainCount == 2, "Two rebels sit in the bunker.");
        if (upgraded) world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_AirBusters");
        Check.That(feeder.FireWeapon(1, Get(world, "AirBusterLauncher").Id.Value, bunker.Id.Value).Accepted, "The bomb is dropped on the bunker.");
        int fx = 0, shock = 0;
        for (var i = 0; i < world.Frames(120); i++)
        {
            // shootDown: the bomb is killed on its way, as a point-defence laser would (docs/GGC-FIXES.md, bunker-buster-only-when-killing-self).
            if (shootDown && i == world.Frames(4) && world.Entities.FirstOrDefault(value => value.Modules.OfType<BunkerBusterBehavior>().Any()) is { } bomb) world.Kill(bomb.Id);
            var result = world.Step();
            foreach (var value in result.Events)
            {
                if (value.Kind == SimEventKind.FxRequested && (value.Text == "AirBustFx" || value.Text == "AirCrashFx")) fx++;
                if (value.Kind == SimEventKind.WeaponImpact && value.Text == "AirShockwave") shock++;
            }
        }
        return (world.Entities.Count(value => value.Template.Name == "AirRebel" && !value.EffectivelyDead), fx, Named(world, "AirEmpWall", 2).Body!.Health < Fixed.FromInt(500) ? 1 : 0);
    }

    /// <summary>
    /// A bunker buster kills the occupants of the bunker it dies in — but only when its owner has the required upgrade — and plays its effects there; without the upgrade the bunker is untouched.
    /// </summary>
    public static void AirBunkerBuster()
    {
        var plain = BusterRun(false);
        Check.That(plain.Alive == 2, $"Without the upgrade the occupants live ({plain.Alive}).");
        var upgraded = BusterRun(true);
        Check.That(upgraded.Alive == 0, $"With the upgrade everybody inside is killed ({upgraded.Alive} left).");
        Check.That(upgraded.Fx >= 1, $"The detonation effect is played ({upgraded.Fx}).");
        Check.That(upgraded.Shock == 1 && plain.Shock == 0, "The shockwave weapon hurt the neighbour of the bunker (and only with the upgrade).");
    }

    /// <summary>
    /// The neutron shell's blast: enemy infantry die, a transport's passengers die, a cliff jumper dies, another vehicle becomes unmanned (neutral and stopped); allies, drones, airborne
    /// aircraft and anything outside the radius are not touched.
    /// </summary>
    public static void AirNeutronBlast()
    {
        var world = AirWeaponWorld(out _, out var feeder);
        feeder.Spawn(2, "AirEmpInfantry", owner: 2, x: 110, y: 100);
        feeder.Spawn(2, "AirTruck", owner: 2, x: 100, y: 130);
        feeder.Spawn(2, "AirBike", owner: 2, x: 90, y: 100);
        feeder.Spawn(2, "AirDrone", owner: 2, x: 100, y: 80);
        feeder.Spawn(2, "AirEmpTank", owner: 2, x: 120, y: 120);
        feeder.Spawn(3, "AirEmpInfantry", owner: 3, x: 105, y: 105);
        feeder.Spawn(2, "AirEmpInfantry", owner: 2, x: 400, y: 100);
        feeder.Spawn(2, "AirTestJet", owner: 2, x: 100, y: 100);
        feeder.Spawn(2, "AirEmpInfantry", owner: 2, x: 100, y: 125);
        feeder.Spawn(2, "AirEmpInfantry", owner: 2, x: 104, y: 125);
        world.Step();
        var truck = Named(world, "AirTruck", 2);
        foreach (var trooper in world.Entities.Where(value => value.Template.Name == "AirEmpInfantry" && value.Y == Fixed.FromInt(125)).ToArray())
            Check.That(feeder.EnterContainer(2, trooper.Id.Value, truck.Id.Value).Accepted, "A trooper boards the truck.");
        world.Step();
        Check.That(truck.Container!.ContainCount == 2, "Two troopers ride in the truck.");
        for (var i = 0; i < world.Frames(70); i++) world.Step();
        var tank = Named(world, "AirEmpTank", 2);
        feeder.SetMoveTarget(2, tank.Id.Value, 300, 300);
        world.Step();
        Check.That(tank.HasMoveTarget, "Setup: the tank is driving.");
        feeder.Spawn(1, "AirNeutronShell", owner: 1, x: 100, y: 100);
        world.Step();
        world.Kill(Get(world, "AirNeutronShell").Id);
        world.Step();
        var inside = world.Entities.Where(value => value.Template.Name == "AirEmpInfantry" && value.Owner.Value == 2 && !value.EffectivelyDead).ToArray();
        Check.That(inside.Length == 1 && inside[0].X == Fixed.FromInt(400), "Every enemy infantry in range died (those in the truck too); the far one lives.");
        Check.That(world.Entities.All(value => value.Template.Name != "AirBike" || value.EffectivelyDead), "A cliff jumper dies.");
        Check.That(!Named(world, "AirDrone", 2).EffectivelyDead && Named(world, "AirDrone", 2).Owner.Value == 2, "A drone is spared.");
        Check.That(!truck.EffectivelyDead && truck.Owner.IsSystem, "The truck is unmanned: neutral.");
        Check.That(tank.Owner.IsSystem && !tank.HasMoveTarget, "The tank is unmanned and stopped.");
        Check.That(!Named(world, "AirEmpInfantry", 3).EffectivelyDead, "Allied infantry is spared (AffectAllies = No).");
        Check.That(!Named(world, "AirTestJet", 2).EffectivelyDead && Named(world, "AirTestJet", 2).Owner.Value == 2, "An aircraft is spared (AffectAirborne = No).");
    }

    /// <summary>
    /// The neutron missile rises straight up from its silo, turns to the point above the target, dives onto it and dies where <c>HeightDieUpdate</c> says; the slow death's two blast
    /// stages then topple/hurt everything around ground zero with falling damage; effects play at launch, ignition and the blast.
    /// </summary>
    public static void AirNeutronMissile()
    {
        var world = AirWeaponWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "AirSilo", owner: 1, x: 200, y: 200);
        feeder.Spawn(2, "AirDummy", owner: 2, x: 600, y: 200);
        feeder.Spawn(2, "AirDummy", owner: 2, x: 660, y: 200);
        feeder.Spawn(2, "AirDummy", owner: 2, x: 710, y: 200);
        feeder.Spawn(2, "AirDummy", owner: 2, x: 900, y: 200);
        world.Step();
        var dummies = world.Entities.Where(value => value.Template.Name == "AirDummy").OrderBy(value => value.X).ToArray();
        Check.That(feeder.FireWeapon(1, Get(world, "AirSilo").Id.Value, dummies[0].Id.Value).Accepted, "The silo launches the neutron missile at the first dummy.");
        var events = new List<string>();
        Entity? missile = null;
        var maxZ = Fixed.Zero;
        long deathTick = -1;
        for (var tick = 0; tick < world.Frames(400); tick++)
        {
            var result = world.Step();
            foreach (var value in result.Events)
                if (value.Kind == SimEventKind.FxRequested) events.Add(value.Text);
            missile ??= world.Entities.FirstOrDefault(value => value.Template.Name == "AirNeutronMissile");
            if (missile is null) continue;
            if (missile.Z > maxZ) maxZ = missile.Z;
            if (tick < world.Frames(6) && missile.Z > Fixed.Zero && missile.Template.Name == "AirNeutronMissile")
                Check.That(missile.X == Fixed.FromInt(200) && missile.Y == Fixed.FromInt(200), $"It leaves the silo straight up (tick {tick}: {missile.X}, {missile.Y}, {missile.Z}).");
            if (deathTick < 0 && missile.EffectivelyDead) deathTick = world.Tick;
            if (world.Tick > world.Frames(300) && !world.Entities.Any(value => value.Template.Name == "AirNeutronMissile")) break;
        }
        Check.That(missile is not null && deathTick > 0, "The missile flew and died.");
        Check.That(maxZ >= Fixed.FromInt(280), $"It climbed to the point above the target ({maxZ}).");
        Check.That((missile!.X - Fixed.FromInt(600)).Abs() < Fixed.FromInt(25) && (missile.Y - Fixed.FromInt(200)).Abs() < Fixed.FromInt(25), $"It came down at the target ({missile.X}, {missile.Y}).");
        Check.That(events.Contains("AirLaunchFx") && events.Contains("AirIgnitionFx") && events.Contains("AirNukeFx"), "Launch, ignition and blast effects were requested: " + string.Join(",", events));
        Check.That(!world.Entities.Any(value => value.Template.Name == "AirNeutronMissile"), "The missile is gone after the slow death.");
        // ground zero: 100 + 50; 60 away: 50 (stage 1 halfway down its ramp) + 50 (stage 2 inside); 110 away: only stage 2's ramp; far: nothing
        Fixed Lost(Entity dummy) => Fixed.FromInt(1000) - dummy.Body!.Health;
        Check.That((Lost(dummies[0]) - Fixed.FromInt(150)).Abs() < Fixed.FromInt(8), $"Ground zero takes both blasts at full strength ({Lost(dummies[0])}).");
        Check.That(Lost(dummies[1]) > Fixed.FromInt(60) && Lost(dummies[1]) < Fixed.FromInt(140), $"60 units away the damage has fallen off ({Lost(dummies[1])}).");
        Check.That(Lost(dummies[2]) > Fixed.FromInt(5) && Lost(dummies[2]) < Fixed.FromInt(60), $"110 units away only the second blast reaches ({Lost(dummies[2])}).");
        Check.That(Lost(dummies[3]) == Fixed.Zero, "Far away nothing happens.");

        // a snapshot in the air restores to the same flight
        var again = AirWeaponWorld(out var fixture2, out var feeder2);
        feeder2.Spawn(1, "AirSilo", owner: 1, x: 200, y: 200);
        feeder2.Spawn(2, "AirDummy", owner: 2, x: 600, y: 200);
        again.Step();
        feeder2.FireWeapon(1, Get(again, "AirSilo").Id.Value, Get(again, "AirDummy").Id.Value);
        for (var i = 0; i < again.Frames(40); i++) again.Step();
        var copy = WorldSnapshot.Restore(WorldSnapshot.Write(again), fixture2.Templates, fixture2.Fingerprint);
        Check.That(copy.StateHash == again.StateHash, "The missile in flight restores to the same hash.");
        for (var i = 0; i < copy.Frames(160); i++) Check.That(copy.Step().StateHash == again.Step().StateHash, "The restored missile flies identically (step " + i + ").");
        _ = fixture;
    }

    /// <summary>
    /// The silo door follows the recharge of the superweapon: closed, opening <c>DoorOpenTime</c> before the power is ready, open when it is; using the power makes it wait, close and stay closed
    /// again, with the effect list of every state requested when it is entered.
    /// </summary>
    public static void AirMissileSiloDoors()
    {
        var world = AirWeaponWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "AirSilo", owner: 1, x: 200, y: 200);
        world.Step();
        var silo = Get(world, "AirSilo");
        var door = silo.Modules.OfType<MissileLauncherBuildingUpdate>().Single();
        var ready = silo.Modules.OfType<SpecialPowerModuleBase>().Single().AvailableOnFrame(world);
        var fx = new List<(long Tick, string Text)>();
        void Run(long untilTick)
        {
            while (world.Tick < untilTick)
            {
                var result = world.Step();
                foreach (var value in result.Events)
                    if (value.Kind == SimEventKind.FxRequested && value.Text.StartsWith("Air", StringComparison.Ordinal)) fx.Add((result.Tick, value.Text));
            }
        }
        Check.That(door.Door == MissileLauncherBuildingUpdate.DoorState.Closed && ready > world.Tick + world.Frames(100), $"The door starts closed with the power recharging (ready at {ready}).");
        Run(ready - world.Frames(40));
        Check.That(door.Door == MissileLauncherBuildingUpdate.DoorState.Closed, "Still closed more than DoorOpenTime (30 ticks) before the power is ready.");
        Run(ready - world.Frames(20));
        Check.That(door.Door == MissileLauncherBuildingUpdate.DoorState.Opening, "Opening 30 ticks before the power is ready.");
        Run(ready + 2);
        Check.That(door.Door == MissileLauncherBuildingUpdate.DoorState.Open, "Open when the power is ready.");
        var use = world.Tick;
        Check.That(feeder.UseSpecialPower(1, silo.Id.Value, "AirNukePower", SpecialPowerTargetKind.Location, x: 700, y: 700).Accepted, "The power is used.");
        Run(use + 3);
        Check.That(door.Door == MissileLauncherBuildingUpdate.DoorState.WaitingToClose, "Using the power starts the wait before the door closes.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash, "The door state restores to the same hash.");
        Run(use + world.Frames(25));
        Check.That(door.Door == MissileLauncherBuildingUpdate.DoorState.Closing, "Closing after DoorWaitOpenTime (15 ticks).");
        Run(use + world.Frames(70));
        Check.That(door.Door == MissileLauncherBuildingUpdate.DoorState.Closed, "Closed again.");
        var texts = fx.Select(value => value.Text).ToArray();
        Check.That(texts.SequenceEqual(["AirDoorFx", "AirOpenFx", "AirLaunchFx", "AirCrashFx", "AirBustFx"]), "One effect per door state, in order: " + string.Join(",", texts));
        for (var i = 0; i < restored.Frames(60); i++) Check.That(restored.Step().StateHash == WorldSnapshot.Restore(WorldSnapshot.Write(restored), fixture.Templates, fixture.Fingerprint).StateHash, "A restored door state is stable.");
    }

    /// <summary>
    /// The gunship inserts toward the target, orbits it at about the orbit radius, its gattling (held until the orbit) and howitzer hurt the enemies in the targeting reticle, it departs after
    /// the orbit time and is removed together with its gattling; the power starts recharging; a gunship killed in the middle takes its gattling with it.
    /// </summary>
    public static void AirSpectreGunship()
    {
        var world = AirWeaponWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "AirGunship", owner: 1, x: 0, y: 0);
        feeder.Spawn(2, "AirDummy", owner: 2, x: 405, y: 5);
        world.Step();
        var ship = Get(world, "AirGunship");
        var spectre = ship.Modules.OfType<SpectreGunshipUpdate>().Single();
        var dummy = Get(world, "AirDummy");
        Check.That(spectre.Status == SpectreGunshipUpdate.GunshipStatus.Idle, "The gunship waits idle.");
        for (var i = 0; i < world.Frames(125); i++) world.Step(); // the power recharges for 4 seconds after the gunship appeared
        var use = feeder.UseSpecialPower(1, ship.Id.Value, "AirGunshipPower", SpecialPowerTargetKind.Location, x: 400, y: 0);
        Check.That(use.Accepted, "The power is used on a location.");
        world.Step();
        Check.That(spectre.Status == SpectreGunshipUpdate.GunshipStatus.Inserting && spectre.Gattling.IsValid, "It is inserting and brought a gattling.");
        var gattling = world.FindEntity(spectre.Gattling);
        Check.That(!feeder.UseSpecialPower(1, ship.Id.Value, "AirGunshipPower", SpecialPowerTargetKind.Location, x: 400, y: 0).Accepted, "The power's recharge started.");
        var fired = 0;
        long orbitStart = -1;
        var minDistance = Fixed.MaxValue;
        var maxDistance = Fixed.Zero;
        var healthBefore = dummy.Body!.Health;
        long attackedAfter = -1;
        for (var tick = 0; tick < world.Frames(300) && spectre.Status != SpectreGunshipUpdate.GunshipStatus.Departing; tick++)
        {
            var result = world.Step();
            gattling ??= world.FindEntity(spectre.Gattling);
            foreach (var value in result.Events)
                if (value.Kind == SimEventKind.WeaponFired && value.Entity == spectre.Gattling) fired++;
            if (spectre.Status == SpectreGunshipUpdate.GunshipStatus.Inserting) Check.That(fired == 0, "The gattling holds its fire during the insertion.");
            if (spectre.Status == SpectreGunshipUpdate.GunshipStatus.Orbiting)
            {
                if (orbitStart < 0) orbitStart = world.Tick;
                if (world.Tick - orbitStart > world.Frames(20))
                {
                    var distance = Fixed.Sqrt((ship.X - Fixed.FromInt(400)) * (ship.X - Fixed.FromInt(400)) + ship.Y * ship.Y);
                    if (distance < minDistance) minDistance = distance;
                    if (distance > maxDistance) maxDistance = distance;
                }
                if (attackedAfter < 0 && dummy.Body.Health < healthBefore) attackedAfter = world.Tick - orbitStart;
            }
        }
        Check.That(orbitStart > 0, "The gunship started to orbit.");
        Check.That(minDistance > Fixed.FromInt(40) && maxDistance < Fixed.FromInt(180), $"It circles the target at about the orbit radius ({minDistance}..{maxDistance}).");
        Check.That(fired > 5, $"The gattling fired ({fired} shots).");
        Check.That(dummy.Body.Health < healthBefore - Fixed.FromInt(50), $"The enemy in the reticle was hurt ({healthBefore} -> {dummy.Body.Health}).");
        Check.That(attackedAfter >= 0, "The attack started while orbiting.");
        Check.That(spectre.Status == SpectreGunshipUpdate.GunshipStatus.Departing && !(gattling is not null && world.FindEntity(gattling.Id) is not null), "After the orbit time it departs and the gattling is gone.");
        var snapshot = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(snapshot.StateHash == world.StateHash, "The departing gunship restores to the same hash.");
        StepUntil(world, () => !world.Entities.Any(value => value.Template.Name == "AirGunship"), 300, "The departing gunship leaves the map and is removed.");

        // a gunship killed mid-orbit takes its gattling with it
        var second = AirWeaponWorld(out _, out var feeder2);
        feeder2.Spawn(1, "AirGunship", owner: 1, x: 0, y: 0);
        second.Step();
        var other = Get(second, "AirGunship");
        for (var i = 0; i < second.Frames(125); i++) second.Step();
        feeder2.UseSpecialPower(1, other.Id.Value, "AirGunshipPower", SpecialPowerTargetKind.Location, x: 400, y: 0);
        for (var i = 0; i < second.Frames(80); i++) second.Step();
        var otherGattling = other.Modules.OfType<SpectreGunshipUpdate>().Single().Gattling;
        Check.That(otherGattling.IsValid && second.FindEntity(otherGattling) is not null, "Setup: the second gunship has its gattling.");
        second.Kill(other.Id);
        for (var i = 0; i < 4; i++) second.Step();
        Check.That(second.FindEntity(otherGattling) is null, "The gattling is deleted with its gunship.");
    }

    /// <summary>
    /// On a map with terrain the carrier's deployment creates the gunship at the edge farthest from the target (pushed back by the orbit radius, facing the target, at the preferred height), the
    /// gunship takes the target over when it appears and strikes; without a terrain nothing is created (the old report), and the whole flight survives a snapshot.
    /// </summary>
    public static void AirSpectreDeployedByCarrier()
    {
        var world = AirWeaponWorld(out var fixture, out var feeder, terrain: true);
        feeder.Spawn(1, "AirGunshipCarrier", owner: 1, x: 300, y: 300);
        feeder.Spawn(2, "AirDummy", owner: 2, x: 1000, y: 1005);
        world.Step();
        var carrier = Get(world, "AirGunshipCarrier");
        for (var i = 0; i < world.Frames(125); i++) world.Step();
        Check.That(world.Terrain is { } terrain && terrain.ExtentX > Fixed.FromInt(1500), "Setup: a map with terrain.");
        Check.That(feeder.UseSpecialPower(1, carrier.Id.Value, "AirGunshipPower", SpecialPowerTargetKind.Location, x: 1000, y: 1000).Accepted, "The carrier's power is used.");
        world.Step();
        var ship = world.Entities.SingleOrDefault(value => value.Template.Name == "AirGunship");
        Check.That(ship is not null, "The gunship was created.");
        Check.That(!feeder.UseSpecialPower(1, carrier.Id.Value, "AirGunshipPower", SpecialPowerTargetKind.Location, x: 1000, y: 1000).Accepted, "The carrier's power is recharging.");
        var spectre = ship!.Modules.OfType<SpectreGunshipUpdate>().Single();
        Check.That(spectre.Status == SpectreGunshipUpdate.GunshipStatus.Inserting && spectre.InitialTarget.X == Fixed.FromInt(1000) && spectre.InitialTarget.Y == Fixed.FromInt(1000), "It knows its target from its first moment.");
        Check.That(ship.Owner.Value == 1 && ship.Z >= Fixed.FromInt(40), $"It belongs to the carrier's owner and flies ({ship.X}, {ship.Y}, {ship.Z}).");
        // farthest from the target (1000, 1000) on a map that is about 2000 wide: the far corner, pushed back by the orbit radius (100)
        Check.That(ship.X < Fixed.Zero || ship.Y < Fixed.Zero || ship.X > world.Terrain!.ExtentX || ship.Y > world.Terrain.ExtentY, $"It starts beyond the edge of the map ({ship.X}, {ship.Y}).");
        var dummy = Get(world, "AirDummy");
        var health = dummy.Body!.Health;
        StepUntil(world, () => spectre.Status == SpectreGunshipUpdate.GunshipStatus.Orbiting, 600, "It reaches the target area and starts to orbit: " + spectre.Status);
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
        Check.That(restored.StateHash == world.StateHash, "The orbiting gunship restores to the same hash.");
        for (var i = 0; i < restored.Frames(120); i++) Check.That(restored.Step().StateHash == world.Step().StateHash, "The restored gunship follows the original (step " + i + ").");
        Check.That(dummy.Body.Health < health, "The gunship hurt the enemy.");
    }
}

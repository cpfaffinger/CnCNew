using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>The hazard-cleaning ambulance with its area power, and the spy-vision update and power.</summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> CleanupAndSpyFiles() => new Dictionary<string, string>(CratesAndSpecialsFiles())
    {
        ["Data/INI/Object/CleanupSpyFixture.ini"] = """
        Weapon CleanupTestWeapon
          PrimaryDamage = 100
          AttackRange = 40.0
          DamageType = HAZARD_CLEANUP
          ClipSize = 0
          DelayBetweenShots = 100
        End
        SpecialPower SPECIAL_CleanupAreaFixture
          Enum = SPECIAL_CLEANUP_AREA
          ReloadTime = 0
        End
        SpecialPower SPECIAL_SpyFixture
          Enum = SPECIAL_SPY_SATELLITE
          ReloadTime = 100
        End
        Upgrade Upgrade_SatelliteOne
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_SatelliteTwo
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End

        Object FixtureAmbulance
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CleanupTestWeapon
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = CleanupHazardUpdate Clean01
            WeaponSlot = PRIMARY
            ScanRate = 100
            ScanRange = 100.0
          End
          Behavior = CleanupAreaPower Area01
            SpecialPowerTemplate = SPECIAL_CleanupAreaFixture
            MaxMoveDistanceFromLocation = 300.0
          End
        End
        Object FixtureHazard
          KindOf = IMMOBILE CLEANUP_HAZARD INERT NO_COLLIDE
          Geometry = CYLINDER
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 3.0
          Body = ActiveBody Body01
            MaxHealth = 120.0
            InitialHealth = 120.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object SpyStation
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = TransportContain Prisoners01
            Slots = 5
            AllowInsideKindOf = INFANTRY
          End
          Behavior = SpyVisionSpecialPower SpyPower01
            SpecialPowerTemplate = SPECIAL_SpyFixture
            BaseDuration = 1000
            BonusDurationPerCaptured = 500
            MaxDuration = 1500
          End
          Behavior = SpyVisionUpdate Spy01
          End
        End
        Object Prisoner
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SatelliteHack
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SpyVisionUpdate SpyOne01
            NeedsUpgrade = Yes
            SelfPowered = Yes
            SpyOnKindof = COMMANDCENTER
            TriggeredBy = Upgrade_SatelliteOne
          End
          Behavior = SpyVisionUpdate SpyTwo01
            NeedsUpgrade = Yes
            SelfPowered = Yes
            SelfPoweredDuration = 1000
            SelfPoweredInterval = 2000
            TriggeredBy = Upgrade_SatelliteTwo
          End
        End
        Object EnemyCenter
          KindOf = STRUCTURE COMMANDCENTER
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          VisionRange = 30.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object EnemyScout
          KindOf = SELECTABLE INFANTRY
          VisionRange = 50.0
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Weapon NetGun
          PrimaryDamage = 10
          AttackRange = 120.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 1000
          RequestAssistRange = 100.0
        End
        Weapon NetAssistGun
          PrimaryDamage = 10
          AttackRange = 300.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 500
          DelayBetweenShots = 100
        End
        Object NetTurret
          KindOf = STRUCTURE SELECTABLE CAN_ATTACK IMMOBILE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY NetGun
            Weapon = SECONDARY NetAssistGun
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = AssistedTargetingUpdate Assist01
            AssistingClipSize = 3
            AssistingWeaponSlot = SECONDARY
            LaserFromAssisted = FX_Test
            LaserToTarget = FX_Test
          End
        End
        Object NetVictim
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static SimulationWorld CleanupWorld(out Feeder feeder)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: CleanupAndSpyFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    // ============================================================================================================ cleanup

    public static void SpecialsCleanupHazard()
    {
        var world = CleanupWorld(out var feeder);
        var ambulance = CsSpawn(world, feeder, "FixtureAmbulance", 1, 0);
        var near = CsSpawn(world, feeder, "FixtureHazard", 0, 70);
        var far = CsSpawn(world, feeder, "FixtureHazard", 0, 400);
        var module = ambulance.Modules.OfType<CleanupHazardUpdate>().Single();

        // Passive: the closest hazard within ScanRange is attacked; the unit walks to three quarters of its weapon range and fires until it is gone.
        CsRun(world, 200, () => !CsExists(world, near));
        Check.That(!CsExists(world, near), "The ambulance cleaned the hazard within its scan range.");
        Check.That(ambulance.X > Fixed.FromInt(25) && ambulance.X < Fixed.FromInt(45), $"It walked to weapon range of the hazard ({ambulance.X}).");
        CsRun(world, 80);
        Check.That(CsExists(world, far) && ambulance.X < Fixed.FromInt(45), "A hazard outside the scan range is left alone, and the unit stays where it is.");

        // The area power: it walks to the point, hunts around it with ScanRange + MaxMoveDistanceFromLocation (here 100 + 300) regardless of its own range, and ends the mode when done.
        var second = CsSpawn(world, feeder, "FixtureHazard", 0, 520);
        var away = CsSpawn(world, feeder, "FixtureHazard", 0, 900);
        Check.That(feeder.UseSpecialPower(1, ambulance.Id.Value, "SPECIAL_CleanupAreaFixture", SpecialPowerTargetKind.Location, x: 450, y: 0).Accepted, "The power accepts a location.");
        world.Step();
        Check.That(module.CleaningArea, "The area mode started.");
        CsRun(world, 600, () => !CsExists(world, far) && !CsExists(world, second));
        Check.That(!CsExists(world, far) && !CsExists(world, second), "Both hazards around the point were cleaned, though one was outside the scan range of where the unit stood.");
        CsRun(world, 600, () => !module.CleaningArea);
        Check.That(!module.CleaningArea, "The unit ended the area mode once nothing was left.");
        Check.That((ambulance.X - Fixed.FromInt(450)).Abs() < Fixed.FromInt(30), $"It went back to the point it was ordered to ({ambulance.X}).");
        Check.That(CsExists(world, away), "A hazard further than ScanRange + move range from the point is untouched.");

        // A fresh order from the player abandons the area.
        var again = CsSpawn(world, feeder, "FixtureHazard", 0, 600);
        feeder.UseSpecialPower(1, ambulance.Id.Value, "SPECIAL_CleanupAreaFixture", SpecialPowerTargetKind.Location, x: 450, y: 0);
        world.Step();
        Check.That(module.CleaningArea, "The area mode runs again.");
        feeder.SetMoveTarget(1, ambulance.Id.Value, 450, 200);
        CsRun(world, 5);
        Check.That(!module.CleaningArea, "A player's order ends the area cleaning.");
        _ = again;

        // A snapshot taken mid-cleaning continues identically.
        var cleanerWorld = CleanupWorld(out var cleanerFeeder);
        var unit = CsSpawn(cleanerWorld, cleanerFeeder, "FixtureAmbulance", 1, 0);
        CsSpawn(cleanerWorld, cleanerFeeder, "FixtureHazard", 0, 90);
        CsSpawn(cleanerWorld, cleanerFeeder, "FixtureHazard", 0, 95, 20);
        CsRun(cleanerWorld, 25);
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(cleanerWorld), new SyntheticFixture(TestRate.Default, files: CleanupAndSpyFiles()).Templates, new SyntheticFixture(TestRate.Default, files: CleanupAndSpyFiles()).Fingerprint);
        for (var tick = 0; tick < cleanerWorld.Frames(200); tick++) Check.That(cleanerWorld.Step().StateHash == restored.Step().StateHash, $"Cleaning continues identically after a restore (tick {tick}).");
        Check.That(cleanerWorld.Entities.All(value => value.Template.Name != "FixtureHazard"), "Both hazards are gone at the end.");
        _ = unit;
    }

    // ============================================================================================================ spy vision

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) SpyWorld()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: CleanupAndSpyFiles());
        var terrain = NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        return (world, new Feeder(world), fixture);
    }

    public static void SpecialsSpyVision()
    {
        var (world, feeder, fixture) = SpyWorld();
        var alpha = new PlayerId(1);
        var beta = new PlayerId(2);
        var station = CsSpawn(world, feeder, "SpyStation", 1, 20, 20);
        var scout = CsSpawn(world, feeder, "EnemyScout", 2, 150, 150);
        var center = CsSpawn(world, feeder, "EnemyCenter", 2, 100, 180);
        var friend = CsSpawn(world, feeder, "EnemyScout", 1, 60, 150);
        // The station's own riders: a building held by somebody takes nobody of another player (canEnterObject, F-61).
        var prisoners = new[] { CsSpawn(world, feeder, "Prisoner", 1, 20, 40), CsSpawn(world, feeder, "Prisoner", 1, 25, 40) };
        foreach (var prisoner in prisoners) Check.That(world.TryEnterContainer(prisoner, station), "A prisoner enters the station.");
        var vision = world.Vision!;
        VisionSettle(world);
        var slot = world.VisionSlotOf(alpha);
        Check.That(!vision.IsVisible(slot, 15, 15), "The enemy scout's surroundings are dark to the spy station's owner.");

        // BaseDuration 1 s + 0.5 s per captured unit (two inside) = 2 s, capped at MaxDuration 1.5 s = 45 ticks.
        var power = station.Modules.OfType<SpyVisionSpecialPower>().Single();
        var spy = station.Modules.OfType<SpyVisionUpdate>().Single();
        Check.That(!spy.Active, "Nothing spies yet.");
        long start = -1;
        for (var tick = 0; tick < world.Frames(200) && !power.IsReady(world); tick++) world.Step();
        Check.That(power.IsReady(world), "The power recharged.");
        Check.That(feeder.UseSpecialPower(1, station.Id.Value, "SPECIAL_SpyFixture").Accepted, "The power is used.");
        world.Step();
        start = world.Tick;
        Check.That(spy.Active, "The spy update is on.");
        Check.That(scout.IsVisionSpiedBy(alpha) && center.IsVisionSpiedBy(alpha), "Every enemy object (of every kind) is looked through.");
        Check.That(!friend.IsVisionSpiedBy(alpha) && !station.IsVisionSpiedBy(alpha) && !scout.IsVisionSpiedBy(beta), "Own objects and the reverse direction are not.");
        VisionSettle(world);
        Check.That(vision.IsVisible(slot, 15, 15), "The owner now sees what the enemy scout sees.");
        
        var bytes = WorldSnapshot.Write(world);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, NavTerrain());
        Check.That(restored.FindEntity(scout.Id)!.IsVisionSpiedBy(alpha), "The reference count survives a snapshot.");
        long ended = -1;
        for (var tick = 0; tick < world.Frames(70); tick++)
        {
            Check.That(world.Step().StateHash == restored.Step().StateHash, $"Spying continues identically after a restore (tick {tick}).");
            if (ended < 0 && !spy.Active) ended = world.Tick;
        }

        Check.That(!spy.Active && !scout.IsVisionSpiedBy(alpha) && !center.IsVisionSpiedBy(alpha), "After MaxDuration the spying ended.");
        Check.That(ended - start >= world.Frames(44) && ended - start <= world.Frames(47), $"The duration was the capped 45 ticks ({ended - start}).");
        VisionSettle(world);
        Check.That(!vision.IsVisible(slot, 15, 15) && vision.IsExplored(slot, 15, 15), "What was seen stays explored but is dark again.");

        // Removing the station while it spies turns the spying off.
        var other = CsSpawn(world, feeder, "SpyStation", 1, 40, 20);
        var otherSpy = other.Modules.OfType<SpyVisionUpdate>().Single();
        otherSpy.ActivateSpyVision(world, 0);
        world.Step();
        Check.That(otherSpy.Active && scout.IsVisionSpiedBy(alpha), "A zero duration spies until switched off.");
        world.Kill(other.Id);
        CsRun(world, 3);
        Check.That(!scout.IsVisionSpiedBy(alpha), "Losing the object that spied releases the objects it looked through.");
    }

    public static void SpecialsSpyVisionUpgrades()
    {
        var (world, feeder, _) = SpyWorld();
        var alpha = new PlayerId(1);
        var station = CsSpawn(world, feeder, "SatelliteHack", 1, 20, 20);
        var center = CsSpawn(world, feeder, "EnemyCenter", 2, 100, 150);
        var scout = CsSpawn(world, feeder, "EnemyScout", 2, 150, 150);
        var modules = station.Modules.OfType<SpyVisionUpdate>().ToArray();
        Check.That(modules.Length == 2 && modules.All(module => !module.Active), "Neither satellite module spies before its upgrade.");

        // The first upgrade: permanent spying on command centers only.
        world.CompleteUpgrade(alpha, null, "Upgrade_SatelliteOne");
        world.Step();
        Check.That(modules[0].Active && !modules[1].Active, "Only the module triggered by the upgrade came on.");
        Check.That(center.IsVisionSpiedBy(alpha) && !scout.IsVisionSpiedBy(alpha), "SpyOnKindof = COMMANDCENTER: the enemy command center is spied on, the scout is not.");
        CsRun(world, 300);
        Check.That(modules[0].Active && center.IsVisionSpiedBy(alpha), "A self-powered module without timers stays on.");

        // The second upgrade: 1 s on, 2 s off, again and again, on every kind.
        world.CompleteUpgrade(alpha, null, "Upgrade_SatelliteTwo");
        CsRun(world, 2);
        Check.That(modules[1].Active && scout.IsVisionSpiedBy(alpha), "The timed module spies on every enemy object.");
        var onTicks = 0;
        var offTicks = 0;
        var cycles = 0;
        var previous = true;
        for (var tick = 0; tick < world.Frames(400); tick++)
        {
            world.Step();
            if (modules[1].Active) onTicks++; else offTicks++;
            if (modules[1].Active && !previous) cycles++;
            previous = modules[1].Active;
        }
        Check.That(cycles >= 3, $"The module switched on again and again ({cycles} times).");
        Check.That(onTicks * 100 / world.Frames(400) is >= 28 and <= 40, $"It spies for about a third of the time, 1 s on and 2 s off ({onTicks} on, {offTicks} off).");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), new SyntheticFixture(TestRate.Default, files: CleanupAndSpyFiles()).Templates, new SyntheticFixture(TestRate.Default, files: CleanupAndSpyFiles()).Fingerprint, NavTerrain());
        for (var tick = 0; tick < world.Frames(200); tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"The cycle continues identically after a restore (tick {tick}).");
    }

    // ============================================================================================================ assisted targeting

    public static void SpecialsAssistedTargeting()
    {
        var world = CleanupWorld(out var feeder);
        var first = CsSpawn(world, feeder, "NetTurret", 1, 0, 0);
        var neighbour = CsSpawn(world, feeder, "NetTurret", 1, 60, 0);
        var distant = CsSpawn(world, feeder, "NetTurret", 1, 500, 0);
        var enemyTurret = CsSpawn(world, feeder, "NetTurret", 2, 30, 0);
        var victim = CsSpawn(world, feeder, "NetVictim", 2, 50, 80);
        Check.That(SimulationWorld.WeaponIndexIn(first, CnCNew.Simulation.Templates.WeaponSlot.Secondary) >= 0 && SimulationWorld.WeaponIndexIn(first, CnCNew.Simulation.Templates.WeaponSlot.Secondary) != SimulationWorld.WeaponIndexIn(first, CnCNew.Simulation.Templates.WeaponSlot.Primary), "The turret has a primary and a secondary weapon.");
        Check.That(world.Weapons[first.Template.DefaultWeapon].RequestAssistRange == Fixed.FromInt(100), "RequestAssistRange is typed on the weapon.");
        var module = neighbour.Modules.OfType<AssistedTargetingUpdate>().Single();
        Check.That(module.IsFreeToAssist(world), "An idle turret is free to assist.");

        Check.That(feeder.FireWeapon(1, first.Id.Value, victim.Id.Value).Accepted, "The first turret opens fire.");
        world.Step();
        Check.That(module.ShotsLeft == 3, "The neighbour within RequestAssistRange was asked for AssistingClipSize shots.");
        Check.That(distant.Modules.OfType<AssistedTargetingUpdate>().Single().ShotsLeft == 0, "A turret beyond the range was not asked.");
        Check.That(enemyTurret.Modules.OfType<AssistedTargetingUpdate>().Single().ShotsLeft == 0, "An enemy turret is never asked.");
        Check.That(!module.IsFreeToAssist(world), "A turret that assists is not free.");
        CsRun(world, 60);
        // 10 from the requester plus three assisting shots (a clip of two, a reload of 15 ticks, one more).
        Check.That(victim.Body!.Health == Fixed.FromInt(960), $"The victim took the requester's shot and the neighbour's three assisting shots ({victim.Body!.Health}).");
        Check.That(module.ShotsLeft == 0 && module.IsFreeToAssist(world), "The neighbour is done and free again.");

        // A request while the neighbour is already assisting is ignored; a snapshot mid-assist continues identically.
        var again = CleanupWorld(out var againFeeder);
        var a = CsSpawn(again, againFeeder, "NetTurret", 1, 0, 0);
        CsSpawn(again, againFeeder, "NetTurret", 1, 60, 0);
        var target = CsSpawn(again, againFeeder, "NetVictim", 2, 50, 80);
        againFeeder.FireWeapon(1, a.Id.Value, target.Id.Value);
        CsRun(again, 6);
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(again), new SyntheticFixture(TestRate.Default, files: CleanupAndSpyFiles()).Templates, new SyntheticFixture(TestRate.Default, files: CleanupAndSpyFiles()).Fingerprint);
        for (var tick = 0; tick < again.Frames(80); tick++) Check.That(again.Step().StateHash == restored.Step().StateHash, $"An assist continues identically after a restore (tick {tick}).");
        Check.That(target.Body!.Health == Fixed.FromInt(960), "The restored run ends with the same damage.");
    }
}

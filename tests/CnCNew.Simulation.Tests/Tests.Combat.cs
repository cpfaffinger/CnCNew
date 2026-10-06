using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static readonly ImmutableArray<PlayerConfiguration> CombatPlayers = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    /// <summary>Weapons with damage radii, a private-weapon emitter and a replaceable outpost. Distances and damages are whole numbers so every assertion is exact.</summary>
    private static IReadOnlyDictionary<string, string> CombatFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/CombatFixture.ini"] = """
        Weapon BlastEnemies
          PrimaryDamage = 40
          PrimaryDamageRadius = 30.0
          SecondaryDamage = 10
          SecondaryDamageRadius = 60.0
          AttackRange = 200.0
          DamageType = EXPLOSION
          RadiusDamageAffects = ENEMIES
          ClipSize = 0
          DelayBetweenShots = 0
        End
        Weapon BlastAll
          PrimaryDamage = 40
          PrimaryDamageRadius = 30.0
          SecondaryDamage = 10
          SecondaryDamageRadius = 60.0
          AttackRange = 200.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End
        Weapon Snipe
          PrimaryDamage = 1
          AttackRange = 200.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End
        Weapon PulseGun
          PrimaryDamage = 5
          PrimaryDamageRadius = 50.0
          AttackRange = 0.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon SelfPulse
          PrimaryDamage = 5
          PrimaryDamageRadius = 50.0
          AttackRange = 0.0
          DamageType = EXPLOSION
          RadiusDamageAffects = SELF ALLIES ENEMIES NEUTRALS
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon TwoShotPulse
          PrimaryDamage = 5
          PrimaryDamageRadius = 50.0
          AttackRange = 0.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 1000
          DelayBetweenShots = 100
        End

        Upgrade Upgrade_Fortify
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End

        Object Soldier
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY BlastEnemies
          End
        End

        Object Gunner
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY BlastAll
          End
        End

        Object Sniper
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY Snipe
          End
          Behavior = FireWeaponUpdate Pulse01
            Weapon = PulseGun
            ExclusiveWeaponDelay = 1000
          End
        End

        Object Bystander
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object FatBystander
          KindOf = SELECTABLE VEHICLE
          Geometry = SPHERE
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 40.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object PoisonField
          KindOf = IMMOBILE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = FireWeaponUpdate Pulse01
            Weapon = PulseGun
            InitialDelay = 600
          End
        End

        Object SelfHarmField
          KindOf = IMMOBILE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = FireWeaponUpdate Pulse01
            Weapon = SelfPulse
          End
        End

        Object ClipField
          KindOf = IMMOBILE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = FireWeaponUpdate Pulse01
            Weapon = TwoShotPulse
          End
        End

        Object Outpost
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ReplaceObjectUpgrade Replace01
            TriggeredBy = Upgrade_Fortify
            ReplaceObject = Fortress
          End
        End

        Object Crushable
          KindOf = INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = CrushDie Crush01
            DeathTypes = NONE +CRUSHED +SPLATTED
            TotalCrushSound = CarAlarm
            TotalCrushSoundPercent = 50
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Fortress
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 900.0
            InitialHealth = 900.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static SimulationWorld CombatWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(SimulationRate.Original, files: CombatFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    private static Fixed HealthOf(SimulationWorld world, string template, int nth = 0) => Get(world, template, nth).Body!.Health;

    // Ids follow the command order (player, sequence), not the spawn order in the test, so victims are found by where they stand.
    private static Entity At(SimulationWorld world, string template, int x, int y)
        => world.Entities.Single(value => value.Template.Name == template && value.X == Fixed.FromInt(x) && value.Y == Fixed.FromInt(y));

    /// <summary>Radius damage: primary/secondary rings around the victim, the enemies-only filter, the bounding sphere shrinking the distance, the shooter never hurt by its own weapon.</summary>
    public static void RadiusDamageRings()
    {
        var world = CombatWorld(out _, out var feeder);
        feeder.Spawn(1, "Soldier", x: 0, y: 0);
        feeder.Spawn(2, "Bystander", x: 100, y: 0); // primary victim, centre of the blast
        feeder.Spawn(2, "Bystander", x: 100, y: 20); // inside the primary radius
        feeder.Spawn(2, "Bystander", x: 100, y: 45); // secondary ring
        feeder.Spawn(2, "Bystander", x: 100, y: 70); // outside both rings
        feeder.Spawn(1, "Bystander", x: 100, y: 10); // an ally, filtered by ENEMIES
        world.Step();
        var shooter = Get(world, "Soldier");
        var victim = At(world, "Bystander", 100, 0);
        Check.That(feeder.FireWeapon(1, shooter.Id.Value, victim.Id.Value).Accepted, "The shot is accepted.");
        var fired = world.Step().Events.Single(value => value.Kind == SimEventKind.WeaponFired);
        Check.That(fired.Value == Fixed.FromInt(40).Raw, "The event reports the primary victim's damage.");
        Check.That(victim.Body!.Health == Fixed.FromInt(60), "The primary victim takes the primary damage.");
        Check.That(At(world, "Bystander", 100, 20).Body!.Health == Fixed.FromInt(60), "A victim inside the primary radius takes the primary damage.");
        Check.That(At(world, "Bystander", 100, 45).Body!.Health == Fixed.FromInt(90), "A victim in the secondary ring takes the secondary damage.");
        Check.That(At(world, "Bystander", 100, 70).Body!.Health == Fixed.FromInt(100), "A victim outside the radius is untouched.");
        Check.That(At(world, "Bystander", 100, 10).Body!.Health == Fixed.FromInt(100), "An ally is filtered by RadiusDamageAffects = ENEMIES.");
        Check.That(shooter.Body!.Health == Fixed.FromInt(100), "The shooter is untouched.");
    }

    /// <summary>The default affects mask hits allies, the primary victim ignores the filters, a fat victim counts from its bounding sphere, and the shooter stays out of it.</summary>
    public static void RadiusDamageDefaultsAndBoundingSphere()
    {
        var world = CombatWorld(out _, out var feeder);
        feeder.Spawn(1, "Gunner", x: 40, y: 0);
        feeder.Spawn(2, "Bystander", x: 100, y: 0);
        feeder.Spawn(1, "Bystander", x: 100, y: 25); // ally, primary ring, hit by the default mask
        feeder.Spawn(2, "FatBystander", x: 100, y: 65); // centre 65 away, 45 from the sphere: secondary ring
        feeder.Spawn(2, "Bystander", x: 100, y: 65); // the same spot without a sphere: outside
        world.Step();
        var shooter = Get(world, "Gunner");
        feeder.FireWeapon(1, shooter.Id.Value, At(world, "Bystander", 100, 0).Id.Value);
        world.Step();
        Check.That(At(world, "Bystander", 100, 0).Body!.Health == Fixed.FromInt(60), "The primary victim is hit.");
        Check.That(At(world, "Bystander", 100, 25).Body!.Health == Fixed.FromInt(60), "An ally is hit by the default mask.");
        Check.That(HealthOf(world, "FatBystander") == Fixed.FromInt(90), "The bounding sphere brings the fat victim into the secondary ring.");
        Check.That(At(world, "Bystander", 100, 65).Body!.Health == Fixed.FromInt(100), "A point victim at the same centre distance is outside.");
        Check.That(shooter.Body!.Health == Fixed.FromInt(100), "The default mask leaves the shooter alone.");
    }

    /// <summary>FireWeaponUpdate: waits InitialDelay, then detonates at its own position on the weapon's cadence; the owner is spared unless SELF is set.</summary>
    public static void FireWeaponUpdateCadence()
    {
        var world = CombatWorld(out _, out var feeder);
        feeder.Spawn(1, "PoisonField", x: 0, y: 0);
        feeder.Spawn(2, "Bystander", x: 30, y: 0);
        feeder.Spawn(2, "Bystander", x: 80, y: 0);
        world.Step();
        var start = world.Tick;
        var field = Get(world, "PoisonField");
        var near = At(world, "Bystander", 30, 0);
        var drops = new List<long>();
        var last = near.Body!.Health;
        for (var i = 0; i < 60; i++)
        {
            world.Step();
            if (near.Body.Health < last) { drops.Add(world.Tick - start); Check.That(last - near.Body.Health == Fixed.FromInt(5), "Every pulse deals the primary damage."); }
            last = near.Body.Health;
        }
        Check.That(drops.Count >= 4, $"The field pulses repeatedly, got {drops.Count} pulses.");
        Check.That(drops[0] >= 18 && drops[0] <= 20, $"InitialDelay 600 ms holds the first pulse back 18 ticks, first pulse after {drops[0]}.");
        for (var i = 1; i < drops.Count; i++) Check.That(drops[i] - drops[i - 1] == 9, "DelayBetweenShots 300 ms is nine ticks.");
        Check.That(At(world, "Bystander", 80, 0).Body!.Health == Fixed.FromInt(100), "A bystander outside the radius is untouched.");
        Check.That(field.Body!.Health == Fixed.FromInt(100), "The default affects mask spares the emitter itself.");

        var selfWorld = CombatWorld(out _, out var selfFeeder);
        selfFeeder.Spawn(1, "SelfHarmField", x: 0, y: 0);
        selfWorld.Step();
        for (var i = 0; i < 30; i++) selfWorld.Step();
        Check.That(HealthOf(selfWorld, "SelfHarmField") < Fixed.FromInt(100), "With SELF in the affects mask the emitter hurts itself.");
    }

    /// <summary>A weapon clip runs dry after ClipSize pulses and reloads for ClipReloadTime.</summary>
    public static void FireWeaponUpdateClip()
    {
        var world = CombatWorld(out _, out var feeder);
        feeder.Spawn(1, "ClipField", x: 0, y: 0);
        feeder.Spawn(2, "Bystander", x: 20, y: 0);
        world.Step();
        var near = Get(world, "Bystander");
        var drops = new List<long>();
        var last = near.Body!.Health;
        var start = world.Tick;
        for (var i = 0; i < 100; i++)
        {
            world.Step();
            if (near.Body.Health < last) drops.Add(world.Tick - start);
            last = near.Body.Health;
        }
        Check.That(drops.Count >= 4, $"The field keeps pulsing, got {drops.Count}.");
        Check.That(drops[1] - drops[0] == 3, $"DelayBetweenShots 100 ms is three ticks inside a clip, got {drops[1] - drops[0]}.");
        Check.That(drops[2] - drops[1] == 30, $"The reload after the second shot takes ClipReloadTime 1000 ms (30 ticks), got {drops[2] - drops[1]}.");
        Check.That(drops[3] - drops[2] == 3, "The next clip starts with the short delay again.");
    }

    /// <summary>ExclusiveWeaponDelay: the object's own real weapon silences the module for that long.</summary>
    public static void FireWeaponUpdateExclusiveDelay()
    {
        var world = CombatWorld(out _, out var feeder);
        feeder.Spawn(1, "Sniper", x: 0, y: 0);
        feeder.Spawn(2, "Bystander", x: 30, y: 0); // inside the pulse radius
        feeder.Spawn(2, "Bystander", x: 120, y: 0); // the sniper's target
        world.Step();
        var sniper = Get(world, "Sniper");
        var near = At(world, "Bystander", 30, 0);
        for (var i = 0; i < 20; i++) world.Step();
        Check.That(near.Body!.Health < Fixed.FromInt(100), "Without a real shot the pulse hits the bystander.");

        Check.That(feeder.FireWeapon(1, sniper.Id.Value, At(world, "Bystander", 120, 0).Id.Value).Accepted, "The real shot is accepted.");
        world.Step();
        var shotTick = world.Tick;
        var frozen = near.Body.Health;
        while (world.Tick < shotTick + 28)
        {
            world.Step();
            Check.That(near.Body.Health == frozen, $"The pulse stays silent right after a real shot (tick {world.Tick - shotTick}).");
        }
        for (var i = 0; i < 20; i++) world.Step();
        Check.That(near.Body.Health < frozen, "The pulse resumes once ExclusiveWeaponDelay has passed.");
    }

    /// <summary>FireWeaponUpdate mid-cadence continues hash-identically from a snapshot.</summary>
    public static void FireWeaponUpdateSnapshot()
    {
        var world = CombatWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "ClipField", x: 0, y: 0);
        feeder.Spawn(2, "Bystander", x: 20, y: 0);
        world.Step();
        for (var i = 0; i < 20; i++) world.Step();
        SnapshotContinues(world, fixture);
    }

    /// <summary>CrushDie is inert without collisions: it binds strictly, keeps the object instantiable and does not disturb an ordinary death.</summary>
    public static void CrushDieIsInert()
    {
        var world = CombatWorld(out _, out var feeder);
        feeder.Spawn(1, "Crushable", x: 0, y: 0);
        world.Step();
        var unit = Get(world, "Crushable");
        Check.That(unit.Template.IsInstantiable, "A unit with CrushDie is instantiable.");
        feeder.Kill(unit.Id.Value);
        for (var i = 0; i < 3; i++) world.Step();
        Check.That(!world.Entities.Any(value => value.Template.Name == "Crushable"), "The unit dies normally; the crush callback never fires without collisions.");
    }

    /// <summary>ReplaceObjectUpgrade: the object is swapped for a fresh one of the replacement template at the same place and owner.</summary>
    public static void ReplaceObjectUpgradeSwapsTemplate()
    {
        var world = CombatWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "Outpost", x: 70, y: 40);
        feeder.GiveMoney(1, 100);
        world.Step();
        var outpost = Get(world, "Outpost");
        feeder.Damage(outpost.Id.Value, 50);
        world.Step();
        Check.That(outpost.Body!.Health == Fixed.FromInt(150), "The outpost is damaged before the upgrade.");
        Check.That(feeder.PurchaseUpgrade(1, "Upgrade_Fortify", target: outpost.Id.Value).Accepted, "The upgrade purchase is accepted.");
        for (var i = 0; i < 10; i++) world.Step();
        Check.That(!world.Entities.Any(value => value.Template.Name == "Outpost"), "The outpost is gone.");
        var fortress = Get(world, "Fortress");
        Check.That(fortress.Owner.Value == 1 && fortress.X == Fixed.FromInt(70) && fortress.Y == Fixed.FromInt(40), "The fortress keeps owner and position.");
        Check.That(fortress.Body!.Health == Fixed.FromInt(900), "The replacement is a fresh object at full health.");
        SnapshotContinues(world, fixture);
    }
}

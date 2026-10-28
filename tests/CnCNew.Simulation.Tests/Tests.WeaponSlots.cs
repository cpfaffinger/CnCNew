using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The weapon slot model: per-slot clips, reload timers and locks, the weapon choice between slots (anti mask, armor, <c>PreferredAgainst</c>,
/// <c>AutoChooseSources</c>), explicit slots and weapon locks, weapon sets selected by veterancy, snapshots and replays. Numbers are chosen so that every
/// damage and tick count is exact (30 ticks per second: 500 ms = 15 ticks, 100 ms = 3, 200 ms = 6, 2000 ms = 60, 3000 ms = 90).
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> WeaponSlotFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/SlotFixture.ini"] = """
        Weapon MainGun
          PrimaryDamage = 40
          AttackRange = 100.0
          DamageType = ARMOR_PIERCING
          ClipSize = 2
          ClipReloadTime = 2000
          DelayBetweenShots = 500
        End
        Weapon MachineGun
          PrimaryDamage = 5
          AttackRange = 60.0
          DamageType = SMALL_ARMS
          ClipSize = 0
          DelayBetweenShots = 100
        End
        Weapon RocketPods
          PrimaryDamage = 60
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 4
          ClipReloadTime = 3000
          DelayBetweenShots = 200
        End
        Weapon BigGun
          PrimaryDamage = 100
          AttackRange = 100.0
          DamageType = ARMOR_PIERCING
          ClipSize = 2
          ClipReloadTime = 3000
          DelayBetweenShots = 500
        End
        Weapon Flak
          PrimaryDamage = 15
          AttackRange = 150.0
          DamageType = EXPLOSION
          AntiGround = No
          AntiAirborneVehicle = Yes
          ClipSize = 0
          DelayBetweenShots = 100
        End

        Armor HulkArmor
          Armor = Default 100%
          Armor = SMALL_ARMS 0%
        End
        Armor GruntArmor
          Armor = Default 100%
          Armor = ARMOR_PIERCING 10%
        End

        Locomotor SlotLoco
          Speed = 60.0
          Acceleration = 30.0
          Braking = 30.0
          TurnRate = 180.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Object Dual
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY MachineGun
          End
        End
        Object Trio
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL SlotLoco
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY MachineGun
            Weapon = TERTIARY RocketPods
            AutoChooseSources = TERTIARY NONE
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ScriptOnly
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY BigGun
            AutoChooseSources = SECONDARY FROM_SCRIPT
          End
        End
        Object AiOnly
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY BigGun
            AutoChooseSources = SECONDARY FROM_AI
          End
        End
        Object Sharing
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY MachineGun
            ShareWeaponReloadTime = Yes
          End
        End
        Object Hunter
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY MachineGun
            PreferredAgainst = SECONDARY INFANTRY
          End
        End
        Object Locked
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY MachineGun
          End
          Behavior = LockWeaponCreate Lock01
            SlotToLock = SECONDARY
          End
        End
        Object Vet
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
          End
          WeaponSet
            Conditions = VETERAN
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY MachineGun
          End
        End
        Object Skyguard
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY Flak
          End
        End
        Object Single
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
          End
        End

        Object Hulk
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          ArmorSet
            Conditions = None
            Armor = HulkArmor
          End
        End
        Object Grunt
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          ArmorSet
            Conditions = None
            Armor = GruntArmor
          End
        End
        Object Rifleman
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        Object Dummy
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        Object Heli
          KindOf = SELECTABLE VEHICLE AIRCRAFT
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) SlotWorld(params (string Template, int Owner, int X, int Y)[] objects)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: WeaponSlotFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        foreach (var (template, owner, x, y) in objects) feeder.Spawn(owner, template, owner: owner, x: x, y: y, z: template == "Heli" ? 100 : 0);
        world.Step();
        return (world, feeder, fixture);
    }

    private static Entity Slotted(SimulationWorld world, string template, int nth = 0) => world.FindEntity(new EntityId(IdOf(world, template, nth)))!;

    private static string[] WeaponsFired(TickResult result, uint source)
        => [..result.Events.Where(value => value.Kind == SimEventKind.WeaponFired && value.Entity.Value == source).Select(value => value.Text)];

    public static void WeaponSlotsTemplateModel()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: WeaponSlotFiles());
        var catalog = fixture.Templates;
        string Name(int weapon) => weapon < 0 ? "-" : catalog.Weapons[weapon].Name;

        catalog.TryGet("Trio", out var trio);
        var set = trio!.WeaponSets.Single();
        Check.That(trio.HasMultipleWeaponSlots && string.Join(",", set.Weapons.Select(Name)) == "MainGun,MachineGun,RocketPods", "A WeaponSet holds three slots: " + string.Join(",", set.Weapons.Select(Name)));
        Check.That(set.AutoChoose[0] == CommandSourceBits.All && set.AutoChoose[1] == CommandSourceBits.All && set.AutoChoose[2] == 0, "AutoChooseSources defaults to every source; NONE clears the slot.");
        Check.That(trio.DefaultWeapon == set.Weapons[0] && trio.DefaultWeaponSet == set, "DefaultWeapon is the primary slot of the default set.");

        catalog.TryGet("ScriptOnly", out var scriptOnly);
        Check.That(scriptOnly!.WeaponSets[0].AutoChoose[1] == CommandSourceBits.FromScript, "FROM_SCRIPT is the script bit.");
        catalog.TryGet("Hunter", out var hunter);
        Check.That(hunter!.WeaponSets[0].PreferredAgainst[1].SequenceEqual(["INFANTRY"]) && hunter.WeaponSets[0].PreferredAgainst[0].IsEmpty, "PreferredAgainst is per slot.");
        catalog.TryGet("Sharing", out var sharing);
        Check.That(sharing!.WeaponSets[0].ShareReloadTime && !trio.WeaponSets[0].ShareReloadTime, "ShareWeaponReloadTime is read per set.");

        catalog.TryGet("Vet", out var vet);
        Check.That(vet!.WeaponSets.Length == 2 && vet.WeaponSets[0].Conditions == 0 && vet.WeaponSets[1].Conditions == 1, "A veteran set carries its condition bit (VETERAN is bit 0).");
        catalog.TryGet("Single", out var single);
        Check.That(!single!.HasMultipleWeaponSlots && single.WeaponSets.Single().WeaponCount == 1, "A single-weapon object has no multi-slot state.");

        // The default anti mask is "ground only"; the Anti* keys set and clear bits.
        var flak = catalog.Weapons.Weapons.Single(weapon => weapon.Name == "Flak");
        Check.That(flak.AntiMask == WeaponInfo.AntiMaskAirborneVehicle && catalog.Weapons.Weapons.Single(weapon => weapon.Name == "MainGun").AntiMask == WeaponInfo.AntiMaskGround, "AntiMask: the ground default and the Flak override.");

        // Nothing falls back silently.
        IReadOnlyDictionary<string, string> With(string weaponSet) => new Dictionary<string, string>(WeaponSlotFiles())
        {
            ["Data/INI/Object/Broken.ini"] = "Object Broken\n  KindOf = SELECTABLE\n  Body = ActiveBody Body01\n  End\n  WeaponSet\n    Conditions = None\n" + weaponSet + "  End\nEnd\n",
        };
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: With("    Weapon = PRIMARY NoSuchWeapon\n")).Templates, "NoSuchWeapon");
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: With("    Weapon = PRIMARY MainGun\n    Frobnicate = Yes\n")).Templates, "Frobnicate");
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: With("    Weapon = PRIMARY MainGun\n    AutoChooseSources = PRIMARY FROM_NOBODY\n")).Templates, "FROM_NOBODY");
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: With("    Weapon = PRIMARY MainGun\n    PreferredAgainst = PRIMARY NOT_A_KIND\n")).Templates, "NOT_A_KIND");
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: With("    Weapon = QUARTERNARY MainGun\n")).Templates, "weapon slot name");
    }

    public static void WeaponSlotsBothSlotsFireAtDifferentTargets()
    {
        var (world, feeder, _) = SlotWorld(("Dual", 1, 0, 0), ("Hulk", 2, 80, 0), ("Dummy", 2, 50, 0));
        var dual = Slotted(world, "Dual");
        var hulk = IdOf(world, "Hulk");
        var dummy = IdOf(world, "Dummy");
        Check.That(dual.ClipOf(0) == 2 && dual.ClipOf(1) == int.MaxValue && dual.ClipOf(2) == -1, "Each slot starts with its own clip (2, unlimited, none).");

        var start = world.Tick;
        Check.That(feeder.FireWeapon(1, dual.Id.Value, hulk).Accepted, "The target at 80 is within the main gun's range only.");
        var first = world.Step();
        Check.That(WeaponsFired(first, dual.Id.Value).SequenceEqual(["MainGun"]), "The main gun fires at it.");
        Check.That(dual.ClipOf(0) == 1 && dual.ReadyTickOf(0) == start + world.Frames(15) && dual.LastFireTickOf(0) == start && dual.CurrentWeaponSlot == 0, "Primary slot: one round used, ready 15 ticks later.");
        Check.That(world.FindEntity(new EntityId(hulk))!.Body!.Health == Fixed.FromInt(100000 - 40), "40 armor-piercing damage against the default armor.");

        Check.That(feeder.FireWeapon(1, dual.Id.Value, dummy).Accepted, "One tick later the main gun is reloading, the machine gun is ready: the order is accepted.");
        var second = world.Step();
        Check.That(WeaponsFired(second, dual.Id.Value).SequenceEqual(["MachineGun"]), "The secondary slot fires at the other target.");
        Check.That(dual.ClipOf(1) == int.MaxValue && dual.ReadyTickOf(1) == start + 1 + world.Frames(3) && dual.LastFireTickOf(1) == start + 1 && dual.CurrentWeaponSlot == 1, "Secondary slot: its own 3-tick delay and last-fire tick.");
        Check.That(dual.ClipOf(0) == 1 && dual.ReadyTickOf(0) == start + world.Frames(15) && dual.LastFireTickOf(0) == start && dual.LastFireTick == start + 1, "The primary slot is untouched; the object's LastFireTick follows any slot.");
        Check.That(world.FindEntity(new EntityId(dummy))!.Body!.Health == Fixed.FromInt(100000 - 5), "5 small-arms damage.");

        // Out of the machine gun's range with the main gun reloading: the object waits instead of firing the wrong weapon.
        Check.That(feeder.FireWeapon(1, dual.Id.Value, hulk) is { Accepted: false, Reason: CommandRejection.WeaponNotReady }, "Hulk at 80: the machine gun is out of range (and cannot hurt a hulk), the main gun reloads.");
    }

    public static void WeaponSlotsBestWeaponAgainstArmor()
    {
        var (world, _, _) = SlotWorld(("Dual", 1, 0, 0), ("Grunt", 2, 40, 0), ("Hulk", 2, 40, 10), ("Dummy", 2, 40, -10));
        var dual = Slotted(world, "Dual");
        Entity Victim(string name) => Slotted(world, name);

        var vsGrunt = world.ChooseWeapon(dual, Victim("Grunt"), FireOrigin.Player);
        Check.That(vsGrunt.CanFire && vsGrunt.Slot == 1, "Against armor that takes only 10 % of armor-piercing damage (4), the machine gun (5) is the better weapon.");
        var vsHulk = world.ChooseWeapon(dual, Victim("Hulk"), FireOrigin.Player);
        Check.That(vsHulk.CanFire && vsHulk.Slot == 0, "A hulk is immune to small arms (0 damage): that slot is not even a candidate, the main gun fires.");
        var vsDummy = world.ChooseWeapon(dual, Victim("Dummy"), FireOrigin.Player);
        Check.That(vsDummy.CanFire && vsDummy.Slot == 0, "Against default armor the heavier weapon (40 against 5) wins; ties would go to the lower slot.");

        // Only the secondary weapon exists on this object: a hulk cannot be fought at all.
        var (world2, feeder2, _) = SlotWorld(("Hunter", 1, 0, 0), ("Hulk", 2, 40, 0));
        var hunter = Slotted(world2, "Hunter");
        var hulk2 = Slotted(world2, "Hulk");
        Check.That(world2.ChooseWeapon(hunter, hulk2, FireOrigin.Player) is { CanFire: true, Slot: 0 }, "A hulk is hit by the primary only.");
        _ = feeder2;

        var (world3, feeder3, _) = SlotWorld(("Skyguard", 1, 0, 0), ("Heli", 2, 50, 0), ("Grunt", 2, 50, 20), ("Single", 1, 0, 30));
        var sky = Slotted(world3, "Skyguard");
        var heli = Slotted(world3, "Heli");
        var grunt = Slotted(world3, "Grunt");
        Check.That(world3.ChooseWeapon(sky, heli, FireOrigin.Player) is { CanFire: true, Slot: 1 }, "An airborne vehicle can only be hit by the anti-air slot.");
        Check.That(world3.ChooseWeapon(sky, grunt, FireOrigin.Player) is { CanFire: true, Slot: 0 }, "A ground target only by the ground slot (the flak gun is anti-air only).");
        Check.That(world3.ChooseWeapon(sky, heli, FireOrigin.Player, WeaponSlot.Primary).Rejection == CommandRejection.InvalidWeaponTarget, "Naming the ground weapon for an airborne target is refused.");
        var single = Slotted(world3, "Single");
        Check.That(world3.ChooseWeapon(single, heli, FireOrigin.Player).Rejection == CommandRejection.InvalidWeaponTarget, "A single ground weapon cannot be aimed at an aircraft either.");
        Check.That(!feeder3.FireWeapon(1, single.Id.Value, heli.Id.Value).Accepted, "...and the command is refused with the reason recorded.");
        Check.That(feeder3.FireWeapon(1, sky.Id.Value, heli.Id.Value).Accepted, "The anti-air slot fires at the helicopter.");
        Check.That(WeaponsFired(world3.Step(), sky.Id.Value).SequenceEqual(["Flak"]), "Flak was the weapon that fired.");
    }

    public static void WeaponSlotsAutoChooseSourcesAndPreferredAgainst()
    {
        var (world, _, _) = SlotWorld(("Trio", 1, 0, 0), ("ScriptOnly", 1, 0, 20), ("AiOnly", 1, 0, 40), ("Hunter", 1, 0, 60), ("Dummy", 2, 50, 0), ("Rifleman", 2, 50, 60), ("Hulk", 2, 50, 60));
        var dummy = Slotted(world, "Dummy");
        Check.That(world.ChooseWeapon(Slotted(world, "Trio"), dummy, FireOrigin.Player).Slot == 0, "The rocket pods (60) would out-damage the gun (40), but AutoChooseSources = TERTIARY NONE keeps them out of the automatic choice.");

        var scriptOnly = Slotted(world, "ScriptOnly");
        Check.That(world.ChooseWeapon(scriptOnly, dummy, FireOrigin.Player).Slot == 0 && world.ChooseWeapon(scriptOnly, dummy, FireOrigin.Ai).Slot == 0, "A FROM_SCRIPT-only weapon is not chosen for a player's or the AI's order...");
        Check.That(world.ChooseWeapon(scriptOnly, dummy, FireOrigin.Script).Slot == 1, "...but is for a script's.");

        // WeaponSet::chooseBestWeaponForTarget tests `okSrcs & CMD_DEFAULT_SWITCH_WEAPON` with the enum value 4, which is the FROM_AI bit: reproduced as shipped.
        var aiOnly = Slotted(world, "AiOnly");
        Check.That(world.ChooseWeapon(aiOnly, dummy, FireOrigin.Ai).Slot == 1 && world.ChooseWeapon(aiOnly, dummy, FireOrigin.Player).Slot == 1 && world.ChooseWeapon(aiOnly, dummy, FireOrigin.Script).Slot == 1,
            "A FROM_AI weapon is chosen by every source (the shipped DEFAULT_SWITCH_WEAPON test reads the FROM_AI bit).");

        // PreferredAgainst: always chosen against its kind, and kept while reloading instead of falling back to the other slot.
        var hunter = Slotted(world, "Hunter");
        var rifleman = Slotted(world, "Rifleman");
        var hulk = Slotted(world, "Hulk");
        Check.That(world.ChooseWeapon(hunter, rifleman, FireOrigin.Player).Slot == 1, "The machine gun is preferred against infantry although the gun does more damage.");
        Check.That(world.ChooseWeapon(hunter, hulk, FireOrigin.Player).Slot == 0, "Against a vehicle the preference does not apply (and the machine gun cannot hurt it).");
    }

    public static void WeaponSlotsPreferredWeaponWaitsWhileReloading()
    {
        var (world, feeder, _) = SlotWorld(("Hunter", 1, 0, 0), ("Rifleman", 2, 40, 0));
        var hunter = Slotted(world, "Hunter");
        var rifleman = IdOf(world, "Rifleman");
        Check.That(feeder.FireWeapon(1, hunter.Id.Value, rifleman).Accepted, "The preferred machine gun fires.");
        Check.That(WeaponsFired(world.Step(), hunter.Id.Value).SequenceEqual(["MachineGun"]), "...against the infantry.");
        Check.That(!feeder.FireWeapon(1, hunter.Id.Value, rifleman).Accepted && hunter.ClipOf(0) == 2, "While the machine gun waits out its 3 ticks the ready main gun is not used: the preferred weapon is kept.");
        for (var wait = 1; wait < world.Frames(3) - 1; wait++) world.Step(); // the rest of the three-frame delay, less the step below
        world.Step();
        Check.That(feeder.FireWeapon(1, hunter.Id.Value, rifleman).Accepted, "Once the machine gun is ready it fires again.");
        Check.That(WeaponsFired(world.Step(), hunter.Id.Value).SequenceEqual(["MachineGun"]) && hunter.ClipOf(0) == 2, "Still the machine gun; the main gun never fired.");
    }

    public static void WeaponSlotsIndependentClipsAndReload()
    {
        var (world, feeder, _) = SlotWorld(("Trio", 1, 0, 0), ("Dummy", 2, 50, 0));
        var trio = Slotted(world, "Trio");
        var dummy = IdOf(world, "Dummy");
        var start = world.Tick;
        // Explicit slots: the gun twice (empties its clip of 2), the machine gun in between.
        Check.That(feeder.FireWeaponSlot(1, trio.Id.Value, dummy, WeaponSlot.Primary).Accepted, "Gun, first round.");
        world.Step();
        Check.That(feeder.FireWeaponSlot(1, trio.Id.Value, dummy, WeaponSlot.Secondary).Accepted, "Machine gun, while the gun waits.");
        world.Step();
        while (world.Tick < start + world.Frames(15)) world.Step();
        Check.That(feeder.FireWeaponSlot(1, trio.Id.Value, dummy, WeaponSlot.Primary).Accepted, "Gun, second round.");
        world.Step();
        Check.That(trio.ClipOf(0) == 0 && trio.ReadyTickOf(0) == start + world.Frames(15 + 60), $"The emptied gun reloads for ClipReloadTime (60 ticks): ready at {trio.ReadyTickOf(0)}.");
        Check.That(trio.ClipOf(1) == int.MaxValue && trio.ReadyTickOf(1) == start + 1 + world.Frames(3) && trio.ClipOf(2) == 4 && trio.ReadyTickOf(2) == 0, "The other slots keep their own clip and timer.");
        // The machine gun keeps firing during the gun's reload; the rocket pods have their own clip of 4.
        Check.That(feeder.FireWeaponSlot(1, trio.Id.Value, dummy, WeaponSlot.Secondary).Accepted && !feeder.FireWeaponSlot(1, trio.Id.Value, dummy, WeaponSlot.Primary).Accepted,
            "The machine gun is available while the gun reloads.");
        world.Step();
        Check.That(feeder.FireWeaponSlot(1, trio.Id.Value, dummy, WeaponSlot.Tertiary).Accepted, "The rocket pods fire independently.");
        world.Step();
        Check.That(trio.ClipOf(2) == 3 && trio.ReadyTickOf(2) == world.Tick - 1 + world.Frames(6), "Rocket pods: one of 4 used, 200 ms (6 ticks) delay.");
        while (world.Tick < start + world.Frames(15 + 60)) world.Step();
        Check.That(feeder.FireWeaponSlot(1, trio.Id.Value, dummy, WeaponSlot.Primary).Accepted, "After 60 ticks the gun's clip is full again and it fires.");
        world.Step();
        Check.That(trio.ClipOf(0) == 1, "A refilled clip of 2 minus the shot.");
    }

    public static void WeaponSlotsSharedReloadTime()
    {
        var (world, feeder, _) = SlotWorld(("Sharing", 1, 0, 0), ("Dummy", 2, 40, 0));
        var sharing = Slotted(world, "Sharing");
        var dummy = IdOf(world, "Dummy");
        var start = world.Tick;
        Check.That(feeder.FireWeaponSlot(1, sharing.Id.Value, dummy, WeaponSlot.Secondary).Accepted, "The machine gun fires.");
        world.Step();
        Check.That(sharing.ReadyTickOf(0) == start + world.Frames(3) && sharing.ReadyTickOf(1) == start + world.Frames(3), "ShareWeaponReloadTime: the gun now waits for the machine gun's delay too.");
        while (world.Tick < start + world.Frames(3)) world.Step();
        Check.That(feeder.FireWeaponSlot(1, sharing.Id.Value, dummy, WeaponSlot.Primary).Accepted, "Both are ready after 3 ticks.");
        world.Step();
        Check.That(sharing.ReadyTickOf(1) == start + world.Frames(3 + 15) && sharing.ReadyTickOf(0) == start + world.Frames(3 + 15), "The gun's 15-tick delay holds the machine gun back as well.");
        Check.That(!feeder.FireWeaponSlot(1, sharing.Id.Value, dummy, WeaponSlot.Secondary).Accepted, "So the machine gun cannot fire in between.");
        // The shared timers are in the snapshot.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), new SyntheticFixture(TestRate.Default, files: WeaponSlotFiles()).Templates, new SyntheticFixture(TestRate.Default, files: WeaponSlotFiles()).Fingerprint);
        var copy = restored.FindEntity(sharing.Id)!;
        Check.That(copy.ReadyTickOf(0) == sharing.ReadyTickOf(0) && copy.ReadyTickOf(1) == sharing.ReadyTickOf(1), "The restored object keeps the shared timers.");
    }

    public static void WeaponSlotsExplicitSlotAndWeaponLock()
    {
        var (world, feeder, _) = SlotWorld(("Trio", 1, 0, 0), ("ScriptOnly", 1, 0, 20), ("Dual", 1, 0, 40), ("Single", 1, 0, 60), ("Dummy", 2, 120, 0), ("Hulk", 2, 40, 20));
        var trio = Slotted(world, "Trio");
        var dummy = IdOf(world, "Dummy");
        var hulk = IdOf(world, "Hulk");

        // The rocket pods (range 150) are the only weapon reaching the target at 120, and only an explicit slot may fire them.
        Check.That(!feeder.FireWeapon(1, trio.Id.Value, dummy).Accepted, "Without a slot nothing may be chosen at 120: the gun and machine gun are too short, the pods are not auto-chosen.");
        Check.That(feeder.FireWeaponSlot(1, trio.Id.Value, dummy, WeaponSlot.Tertiary).Accepted, "A command button naming the tertiary slot fires the pods.");
        Check.That(WeaponsFired(world.Step(), trio.Id.Value).SequenceEqual(["RocketPods"]), "The pods fired.");
        Check.That(trio.WeaponLock == WeaponLockKind.Temporary && trio.CurrentWeaponSlot == 2, "The button pinned the slot (setWeaponLock LOCKED_TEMPORARILY).");

        // A move order releases the temporary lock.
        Check.That(feeder.SetMoveTarget(1, trio.Id.Value, 5, 5).Accepted, "A move order is accepted.");
        world.Step();
        Check.That(trio.WeaponLock == WeaponLockKind.None, "A move order released the temporary lock.");

        // Slots that do not exist, ranges and armor are refused with their own reason.
        var dual = Slotted(world, "Dual");
        Check.That(feeder.FireWeaponSlot(1, dual.Id.Value, hulk, WeaponSlot.Tertiary) is { Accepted: false, Reason: CommandRejection.NoWeapon }, "An empty slot has no weapon.");
        Check.That(feeder.FireWeaponSlot(1, Slotted(world, "Single").Id.Value, hulk, WeaponSlot.Secondary) is { Accepted: false, Reason: CommandRejection.NoWeapon }, "A single-weapon object has only a primary slot.");
        Check.That(feeder.FireWeaponSlot(1, dual.Id.Value, hulk, WeaponSlot.Secondary) is { Accepted: false, Reason: CommandRejection.InvalidWeaponTarget }, "The machine gun cannot hurt a hulk.");
        Check.That(feeder.FireWeaponSlot(1, dual.Id.Value, dummy, WeaponSlot.Primary) is { Accepted: false, Reason: CommandRejection.OutOfRange }, "The gun is out of range at 120.");

        // A temporary lock ends when the clip empties: BigGun has a clip of 2.
        var scripted = Slotted(world, "ScriptOnly");
        var near = IdOf(world, "Hulk");
        Check.That(feeder.FireWeaponSlot(1, scripted.Id.Value, near, WeaponSlot.Secondary).Accepted, "The big gun fires on an explicit order.");
        world.Step();
        Check.That(scripted.WeaponLock == WeaponLockKind.Temporary && scripted.ClipOf(1) == 1, "Locked after the first round.");
        for (var i = 0; i < world.Frames(15); i++) world.Step();
        Check.That(feeder.FireWeapon(1, scripted.Id.Value, near).Accepted, "An ordinary order while locked uses the locked slot.");
        Check.That(WeaponsFired(world.Step(), scripted.Id.Value).SequenceEqual(["BigGun"]), "...the big gun, although automatic choice would never pick it for a player.");
        Check.That(scripted.ClipOf(1) == 0 && scripted.WeaponLock == WeaponLockKind.None, "The emptied clip released the temporary lock.");

        // SetWeaponLockCommand.
        Check.That(feeder.SetWeaponLock(1, dual.Id.Value, WeaponSlot.Secondary, WeaponLockKind.Permanent).Accepted, "Lock the secondary slot permanently.");
        world.Step();
        Check.That(dual.WeaponLock == WeaponLockKind.Permanent && dual.CurrentWeaponSlot == 1, "Permanent lock on slot 1.");
        Check.That(feeder.SetWeaponLock(1, dual.Id.Value, WeaponSlot.Primary, WeaponLockKind.Temporary).Accepted, "A temporary lock request is accepted...");
        world.Step();
        Check.That(dual.WeaponLock == WeaponLockKind.Permanent && dual.CurrentWeaponSlot == 1, "...but never overrides the permanent one.");
        Check.That(feeder.SetWeaponLock(1, dual.Id.Value, WeaponSlot.Tertiary, WeaponLockKind.Permanent) is { Accepted: false, Reason: CommandRejection.NoWeapon }, "Locking an empty slot is refused.");
        Check.That(world.ChooseWeapon(dual, Slotted(world, "Hulk"), FireOrigin.Player).Rejection == CommandRejection.InvalidWeaponTarget, "With the machine gun locked, a hulk cannot be attacked.");
        Check.That(feeder.SetWeaponLock(1, dual.Id.Value, 0, WeaponLockKind.None).Accepted, "Release.");
        world.Step();
        Check.That(dual.WeaponLock == WeaponLockKind.None && world.ChooseWeapon(dual, Slotted(world, "Hulk"), FireOrigin.Player).CanFire, "Released: the gun is chosen again.");
    }

    public static void WeaponSlotsLockWeaponCreateAndVeteranSets()
    {
        var (world, feeder, _) = SlotWorld(("Locked", 1, 0, 0), ("Vet", 1, 0, 20), ("Dummy", 2, 40, 0), ("Hulk", 2, 40, 20));
        var locked = Slotted(world, "Locked");
        Check.That(locked.WeaponLock == WeaponLockKind.Permanent && locked.CurrentWeaponSlot == 1, "LockWeaponCreate pinned the secondary slot permanently when the object appeared.");
        Check.That(world.ChooseWeapon(locked, Slotted(world, "Dummy"), FireOrigin.Ai).Slot == 1, "The pinned machine gun is used although the gun would do more damage.");
        Check.That(feeder.FireWeaponSlot(1, locked.Id.Value, IdOf(world, "Dummy"), WeaponSlot.Primary).Accepted && WeaponsFired(world.Step(), locked.Id.Value).SequenceEqual(["MachineGun"]),
            "A permanent lock beats an explicit slot.");

        var vet = Slotted(world, "Vet");
        Check.That(vet.ClipOf(0) == 2 && vet.ClipOf(1) == -1, "A regular Vet has only the primary weapon in its default set.");
        Check.That(world.ChooseWeapon(vet, Slotted(world, "Dummy"), FireOrigin.Player).Slot == 0 && feeder.FireWeaponSlot(1, vet.Id.Value, IdOf(world, "Dummy"), WeaponSlot.Secondary).Reason == CommandRejection.NoWeapon, "...and nothing in the secondary slot.");
        // Use the gun once so that the rebuild is visible.
        Check.That(feeder.FireWeapon(1, vet.Id.Value, IdOf(world, "Dummy")).Accepted, "Fire the gun.");
        world.Step();
        Check.That(vet.ClipOf(0) == 1, "One round used.");
        world.SetVeterancy(vet.Id, 1);
        Check.That(vet.ClipOf(0) == 2 && vet.ClipOf(1) == int.MaxValue && vet.ReadyTickOf(0) == world.Tick && vet.ReadyTickOf(1) == world.Tick,
            "Rank VETERAN selects the WeaponSet Conditions = VETERAN: both weapons are built fresh, loaded and ready.");
        Check.That(feeder.FireWeaponSlot(1, vet.Id.Value, IdOf(world, "Dummy"), WeaponSlot.Secondary).Accepted && WeaponsFired(world.Step(), vet.Id.Value).SequenceEqual(["MachineGun"]), "The veteran machine gun fires.");
        world.SetVeterancy(vet.Id, 0);
        Check.That(vet.ClipOf(1) == -1 && vet.WeaponLock == WeaponLockKind.None && vet.CurrentWeaponSlot == 0, "Back to regular: the secondary weapon is gone, the explicit slot's lock is released and the primary is current.");
    }

    public static void WeaponSlotsSnapshotMidReloadAndDeterminism()
    {
        var (first, firstFeeder, fixture) = SlotWorld(("Trio", 1, 0, 0), ("Sharing", 1, 0, 30), ("Dummy", 2, 50, 0), ("Rifleman", 2, 50, 30));
        var (second, secondFeeder, _) = SlotWorld(("Trio", 1, 0, 0), ("Sharing", 1, 0, 30), ("Dummy", 2, 50, 0), ("Rifleman", 2, 50, 30));
        // Every tick both objects shoot at whatever the model lets them (rejections are fine); now and then the pods are ordered by slot.
        List<ulong> Run(SimulationWorld world, Feeder feeder, int frames)
        {
            var hashes = new List<ulong>();
            for (var i = 0; i < world.Frames(frames); i++)
            {
                feeder.FireWeapon(1, IdOf(world, "Trio"), IdOf(world, "Dummy"));
                feeder.FireWeapon(1, IdOf(world, "Sharing"), IdOf(world, "Rifleman"));
                if (world.Tick % world.Frames(40) == world.Frames(10)) feeder.FireWeaponSlot(1, IdOf(world, "Trio"), IdOf(world, "Dummy"), WeaponSlot.Tertiary);
                hashes.Add(world.Step().StateHash);
            }
            return hashes;
        }
        Check.That(Run(first, firstFeeder, 40).SequenceEqual(Run(second, secondFeeder, 40)), "Two independent runs agree for 40 ticks.");
        var trio = Slotted(first, "Trio");
        Check.That(trio.ClipOf(0) < 2 || trio.ReadyTickOf(0) > first.Tick, $"The gun has fired by now (clip {trio.ClipOf(0)}, ready {trio.ReadyTickOf(0)}, tick {first.Tick}).");

        var bytes = WorldSnapshot.Write(first);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "The snapshot round-trips byte-identically.");
        var restoredFeeder = firstFeeder.ForkTo(restored);
        var a = Run(first, firstFeeder, 200);
        var b = Run(restored, restoredFeeder, 200);
        var c = Run(second, secondFeeder, 200);
        Check.That(a.SequenceEqual(b), "A snapshot taken mid-reload continues identically.");
        Check.That(a.SequenceEqual(c), "The independent run produces the same hashes.");
        foreach (var name in new[] { "Trio", "Sharing" })
        {
            var live = Slotted(first, name);
            var copy = Slotted(restored, name);
            for (var slot = 0; slot < WeaponSlot.Count; slot++)
                Check.That(live.ClipOf(slot) == copy.ClipOf(slot) && live.ReadyTickOf(slot) == copy.ReadyTickOf(slot) && live.LastFireTickOf(slot) == copy.LastFireTickOf(slot), $"{name} slot {slot} survives the snapshot.");
            Check.That(live.CurrentWeaponSlot == copy.CurrentWeaponSlot && live.WeaponLock == copy.WeaponLock, $"{name}: current slot and lock survive the snapshot.");
        }

        // Single-weapon objects keep their pre-slot snapshot shape: nothing about slots is written for them.
        var (single, _, _) = SlotWorld(("Single", 1, 0, 0));
        Check.That(!System.Text.Encoding.UTF8.GetString(WorldSnapshot.Write(single)).Contains("weaponSlots", StringComparison.Ordinal), "A single-weapon object writes no weapon slot block.");
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Check.That(text.Contains("weaponSlots", StringComparison.Ordinal), "A multi-weapon object does.");
        // A snapshot whose weapon state does not match the template is refused.
        var stripped = System.Text.Encoding.UTF8.GetBytes(text.Replace("\"weaponSlots\"", "\"weaponSlotz\"", StringComparison.Ordinal));
        Check.Throws<Exception>(() => WorldSnapshot.Restore(stripped, fixture.Templates, fixture.Fingerprint), "weapon slot");
    }

    public static void WeaponSlotsCommandsRoundTripInReplays()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: WeaponSlotFiles());
        var player1 = new PlayerId(1);
        var player2 = new PlayerId(2);
        var commands = new List<SimCommand>
        {
            new SpawnCommand(player1, 1, 0, "Trio", player1, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero),
            new SpawnCommand(player1, 2, 0, "Locked", player1, Fixed.Zero, Fixed.FromInt(30), Fixed.Zero, Fixed.Zero),
            new SpawnCommand(player2, 1, 0, "Dummy", player2, Fixed.FromInt(50), Fixed.Zero, Fixed.Zero, Fixed.Zero),
        };
        ulong sequence = 2;
        for (var tick = 2; tick < 150; tick++)
        {
            commands.Add(new FireWeaponCommand(player1, ++sequence, tick, new EntityId(1), new EntityId(3), tick % 20 == 7 ? WeaponSlot.Tertiary : WeaponSlot.Best, tick % 3 == 0 ? FireOrigin.Ai : FireOrigin.Player));
            if (tick % 5 == 0) commands.Add(new FireWeaponCommand(player1, ++sequence, tick, new EntityId(2), new EntityId(3)));
            if (tick == 60) commands.Add(new SetWeaponLockCommand(player1, ++sequence, tick, new EntityId(1), WeaponSlot.Secondary, WeaponLockKind.Permanent));
            if (tick == 100) commands.Add(new SetWeaponLockCommand(player1, ++sequence, tick, new EntityId(1), 0, WeaponLockKind.None));
        }
        var ordered = commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence).ToImmutableArray();
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, fixture.Fingerprint, SyntheticFixture.FixtureId, 1, new WorldId(1), TwoPlayers, 160, 20);
        var probe = ReplayRunner.Run(new ReplayFile(header, ordered, []), fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        var file = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, ordered, probe.Checkpoints)));
        Check.That(file.Commands.SequenceEqual(ordered), "Fire commands with slot and origin and weapon lock commands survive the replay file.");
        var again = ReplayRunner.Run(file, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(again.TickHashes.AsSpan().SequenceEqual(probe.TickHashes) && again.FinalHash == probe.FinalHash && again.VerifiedCheckpoints == file.Checkpoints.Length, "The replay reproduces itself tick by tick and meets its checkpoints.");
        var world = ReplayRunner.CreateWorld(header, fixture.Templates);
        ReplayRunner.Run(file, world);
        var dummy = world.FindEntity(new EntityId(3))!;
        Check.That(dummy.Body!.Health < dummy.Body.MaxHealth && probe.TickHashes.Distinct().Count() > 20, "The scripted shots did damage.");
        Check.That(world.RejectionCounts[(int)CommandRejection.WeaponNotReady] > 0 || probe.RejectedCommands > 0, "Orders that cannot fire are rejected, not silently dropped.");

        var round = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, 30, new string('A', 64), "fixture", 1, new WorldId(1), TwoPlayers, 10, 5);
        ImmutableArray<SimCommand> samples =
        [
            new FireWeaponCommand(player1, 1, 3, new EntityId(4), new EntityId(5)),
            new FireWeaponCommand(player1, 2, 4, new EntityId(4), new EntityId(5), WeaponSlot.Tertiary, FireOrigin.Script),
            new FireWeaponCommand(player2, 1, 4, new EntityId(6), new EntityId(5), WeaponSlot.Best, FireOrigin.Ai),
            new SetWeaponLockCommand(player1, 3, 5, new EntityId(4), WeaponSlot.Secondary, WeaponLockKind.Permanent),
            new SetWeaponLockCommand(player1, 4, 6, new EntityId(4), 0, WeaponLockKind.None),
        ];
        Check.That(ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(round, samples, []))).Commands.SequenceEqual(samples), "Every combination round-trips.");
        Check.That(ReplayFormat.CurrentVersion == 16 && CommandTypes.Version == 18 && WorldSnapshot.Version == 10, "Weapon slots: replay format 16, command table 18, snapshot 10 (the formats of 24 fraction bits).");
        var bad = ReplayFormat.Write(new ReplayFile(round, [new FireWeaponCommand(player1, 1, 3, new EntityId(4), new EntityId(5), 7, FireOrigin.Ai)], []));
        Check.Throws<ReplayFormatException>(() => ReplayFormat.Read(bad), "slot");
    }

    public static void WeaponSlotsScriptCommandButtonsHonourTheSlot()
    {
        var extra = new Dictionary<string, string>
        {
            ["Data/INI/Object/SlotScript.ini"] = """
            Weapon SlotBig
              PrimaryDamage = 50
              AttackRange = 80.0
              DamageType = EXPLOSION
              ClipSize = 2
              ClipReloadTime = 3000
              DelayBetweenShots = 400
            End
            Object AlphaDual
              Side = Alpha
              KindOf = SELECTABLE INFANTRY
              VisionRange = 120.0
              ShroudClearingRange = 120.0
              Locomotor = SET_NORMAL SkirmishLoco
              Body = ActiveBody Body01
                MaxHealth = 80.0
                InitialHealth = 80.0
              End
              WeaponSet
                Conditions = None
                Weapon = PRIMARY SkirmishGun
                Weapon = SECONDARY SlotBig
                AutoChooseSources = SECONDARY NONE
              End
              Behavior = AIUpdateInterface AI01
              End
              Behavior = DestroyDie Destroy01
              End
            End
            CommandButton Cmd_AlphaBig
              Command = FIRE_WEAPON
              WeaponSlot = SECONDARY
            End
            CommandButton Cmd_AlphaSwitchBig
              Command = SWITCH_WEAPON
              WeaponSlot = SECONDARY
            End
            """,
        };
        var session = StartMission([
            Scr.Script("Fire", [Scr.And(Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY_ON_NAMED", Scr.Unit("Shooter"), Scr.Ability("Cmd_AlphaBig"), Scr.Unit("Victim"))], oneShot: true),
            Scr.Script("Switch", [Scr.And(Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY", Scr.Unit("Switcher"), Scr.Ability("Cmd_AlphaSwitchBig"))], oneShot: true),
        ], objects: [Placed("AlphaDual", 100m, 100m, "teamA", "Shooter"), Placed("BetaSoldier", 130m, 100m, "teamB", "Victim"), Placed("AlphaDual", 100m, 300m, "teamA", "Switcher")],
        options: MissionOptions.Standard with { CombatIntervalTicks = 0 }, extraFiles: extra);
        var victim = NamedIn(session, "Victim")!;
        var shooter = NamedIn(session, "Shooter")!;
        var switcher = NamedIn(session, "Switcher")!;
        Run(session, session.Frames(3)); // vision first: a FIRE_WEAPON order at a target the player cannot see is refused
        var before = victim.Body!.Health;
        session.Engine.SignalUiInteraction("go");
        Run(session, session.Frames(3));
        Check.That(before - victim.Body.Health == Fixed.FromInt(50), $"A FIRE_WEAPON button names the SECONDARY slot: the carrier shoots SlotBig (50), not its primary gun (20): {before - victim.Body.Health} dealt; diagnostics: {string.Join("; ", session.Engine.Diagnostics.Keys)}; rejections: {string.Join(",", session.World.RejectionCounts.Select((count, index) => count > 0 ? $"{(CommandRejection)index}={count}" : ""))}.");
        Check.That(shooter.WeaponLock == WeaponLockKind.Temporary && shooter.CurrentWeaponSlot == 1, "...and the button pinned that slot.");
        Check.That(switcher.WeaponLock == WeaponLockKind.Permanent && switcher.CurrentWeaponSlot == 1, "A SWITCH_WEAPON button locks its slot permanently.");
    }
}

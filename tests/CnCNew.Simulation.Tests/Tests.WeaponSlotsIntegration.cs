using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// The weapon slot model as the single source of "which weapon does an object fire" for the modules that select a weapon set on their own: the car bomb, the salvage-crate
/// weapon upgrade, <c>FireOCLAfterWeaponCooldownUpdate</c> (watching a secondary slot), <c>AssistedTargetingUpdate</c> (the real secondary clip) and the airfield reload of every
/// <c>RETURN_TO_BASE</c> slot.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> WeaponSlotIntegrationFiles() => new Dictionary<string, string>(WeaponSlotFiles())
    {
        ["Data/INI/Object/SlotIntegration.ini"] = """
        Weapon Toxin
          PrimaryDamage = 5
          AttackRange = 100.0
          DamageType = POISON
          ClipSize = 3
          ClipReloadTime = 1000
          DelayBetweenShots = 300
        End
        ObjectCreationList OCL_SlotCloud
          CreateObject
            ObjectNames = SlotCloud
          End
        End
        Object SlotCloud
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object SlotScud
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY Toxin
          End
          Behavior = FireOCLAfterWeaponCooldownUpdate Cloud01
            WeaponSlot = SECONDARY
            OCL = OCL_SlotCloud
            MinShotsToCreateOCL = 2
            OCLLifetimePerSecond = 1000
            OCLLifetimeMaxCap = 5000
          End
        End

        Object SlotCar
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
          End
          WeaponSet
            Conditions = CARBOMB
            Weapon = PRIMARY BigGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object SlotTerrorist
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL SlotLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = ConvertToCarBombCrateCollide CarBomb01
            RequiredKindOf = VEHICLE
          End
        End

        Object SlotCrate
          KindOf = CRATE
          Geometry = BOX
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 4.0
          Behavior = SalvageCrateCollide Salvage01
            ForbiddenKindOf = PROJECTILE
            WeaponChance = 100%
            LevelChance = 0%
            MoneyChance = 0%
            MinMoney = 40
            MaxMoney = 40
          End
        End
        Object SlotSalvager
          KindOf = SELECTABLE VEHICLE SALVAGER WEAPON_SALVAGER
          Locomotor = SET_NORMAL SlotLoco
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MainGun
            Weapon = SECONDARY MachineGun
          End
          WeaponSet
            Conditions = CRATEUPGRADE_ONE
            Weapon = PRIMARY BigGun
            Weapon = SECONDARY MachineGun
          End
          WeaponSet
            Conditions = CRATEUPGRADE_TWO
            Weapon = PRIMARY RocketPods
            Weapon = SECONDARY MachineGun
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) SlotIntegrationWorld(params (string Template, int Owner, int X, int Y)[] objects)
    {
        var fixture = new SyntheticFixture(files: WeaponSlotIntegrationFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        foreach (var (template, owner, x, y) in objects) feeder.Spawn(owner, template, owner: owner, x: x, y: y);
        world.Step();
        return (world, feeder, fixture);
    }

    private static string SetOf(SimulationWorld world, Entity entity)
    {
        var set = SimulationWorld.ActiveWeaponSet(entity);
        return set is null ? "-" : string.Join(",", set.Weapons.Select(index => index < 0 ? "-" : world.Weapons[index].Name));
    }

    /// <summary>
    /// A vehicle with a <c>CARBOMB</c> weapon set takes it when a terrorist converts it: the flag is raised with <c>IS_CARBOMB</c>, the slot state is rebuilt for the new set (a full clip, the
    /// slot the old set had is gone) and the attack choice fires the bomb weapon.
    /// </summary>
    public static void WeaponSlotsCarBombSetFeedsTheSlots()
    {
        var (world, feeder, fixture) = SlotIntegrationWorld(("SlotCar", 2, 0, 0), ("SlotTerrorist", 1, 60, 0), ("Dummy", 2, 40, 90));
        var car = Slotted(world, "SlotCar");
        var terrorist = Slotted(world, "SlotTerrorist");
        var dummy = Slotted(world, "Dummy");
        Check.That(SetOf(world, car) == "MainGun,MachineGun,-" && SimulationWorld.ActiveWeaponSet(car)!.Index == 0, "Before the conversion the car has its two-weapon default set: " + SetOf(world, car));
        Check.That(car.ClipOf(1) == int.MaxValue, "The machine gun slot is loaded.");

        CsAi(terrorist).OrderApproachGoal(world, car, false);
        CsRun(world, 200, () => !CsExists(world, terrorist));
        Check.That(!CsExists(world, terrorist) && car.Owner == new PlayerId(1) && car.HasStatus(ObjectStatusBits.Index("IS_CARBOMB")), "The terrorist converted the car.");
        Check.That(SetOf(world, car) == "BigGun,-,-", "The active weapon set is the CARBOMB set: " + SetOf(world, car));
        Check.That(SimulationWorld.ActiveWeaponSet(car)!.Index == 1 && car.ClipOf(0) == 2 && car.ClipOf(1) == -1, $"The slots were rebuilt for it (set {SimulationWorld.ActiveWeaponSet(car)!.Index}, clips {car.ClipOf(0)}/{car.ClipOf(1)}).");

        var health = dummy.Body!.Health;
        Check.That(feeder.FireWeapon(1, car.Id.Value, dummy.Id.Value).Accepted, "The bomb car attacks.");
        var fired = WeaponsFired(world.Step(), car.Id.Value);
        Check.That(fired.SequenceEqual(["BigGun"]) && dummy.Body!.Health == health - Fixed.FromInt(100), "It fires the CARBOMB weapon, not its old guns: " + string.Join(",", fired));

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        for (var tick = 0; tick < 40; tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"The converted car continues identically after a restore (tick {tick}).");
    }

    /// <summary>
    /// A weapon salvager that picks up a crate raises <c>CRATEUPGRADE_ONE</c>: the slot model switches to that set (both slots rebuilt: the new primary with its own clip), a second crate raises
    /// <c>_TWO</c>, and the attack choice fires from the upgraded weapon.
    /// </summary>
    public static void WeaponSlotsCrateUpgradeSetsFeedTheSlots()
    {
        var (world, feeder, fixture) = SlotIntegrationWorld(("SlotSalvager", 1, 0, 0), ("Dummy", 2, 0, 80));
        var tank = Slotted(world, "SlotSalvager");
        var dummy = Slotted(world, "Dummy");
        Check.That(SetOf(world, tank) == "MainGun,MachineGun,-" && SimulationWorld.WeaponSetFlagsOf(tank) == 0, "A fresh salvager uses its default set.");

        void Pickup(int x)
        {
            feeder.Spawn(0, "SlotCrate", owner: 0, x: x, y: 0);
            feeder.SetMoveTarget(1, tank.Id.Value, x + 70, 0);
            var level = tank.CrateWeaponLevel;
            CsRun(world, 100, () => tank.CrateWeaponLevel != level);
            feeder.SetMoveTarget(1, tank.Id.Value, 0, 0);
            CsRun(world, 100, () => tank.X < Fixed.FromInt(2));
        }

        Pickup(30);
        Check.That(tank.CrateWeaponLevel == 1 && SetOf(world, tank) == "BigGun,MachineGun,-", "The first crate selects CRATEUPGRADE_ONE: " + SetOf(world, tank));
        Check.That(SimulationWorld.ActiveWeaponSet(tank)!.Index == 1 && tank.ClipOf(0) == 2 && tank.ClipOf(1) == int.MaxValue, "Its slots were rebuilt for the new set with full clips.");
        var health = dummy.Body!.Health;
        Check.That(feeder.FireWeapon(1, tank.Id.Value, dummy.Id.Value).Accepted, "The salvager attacks.");
        var fired = WeaponsFired(world.Step(), tank.Id.Value);
        Check.That(fired.SequenceEqual(["BigGun"]) && dummy.Body!.Health == health - Fixed.FromInt(100) && tank.ClipOf(0) == 1, "The upgraded weapon fires and spends a round of its own clip: " + string.Join(",", fired));

        Pickup(30);
        Check.That(tank.CrateWeaponLevel == 2 && SetOf(world, tank) == "RocketPods,MachineGun,-" && SimulationWorld.ActiveWeaponSet(tank)!.Index == 2 && tank.ClipOf(0) == 4, "The second crate selects CRATEUPGRADE_TWO with a full clip: " + SetOf(world, tank));

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash && SetOf(restored, restored.FindEntity(tank.Id)!) == "RocketPods,MachineGun,-", "The upgraded set survives a snapshot.");
        for (var tick = 0; tick < 40; tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"The upgraded salvager continues identically after a restore (tick {tick}).");
    }

    /// <summary>
    /// <c>FireOCLAfterWeaponCooldownUpdate</c> watching the <em>secondary</em> slot (every shipped use does): after a series of shots of that slot it creates its list once the weapon could
    /// have fired again and did not, or the moment the object switches to another weapon; a single shot is below <c>MinShotsToCreateOCL</c> and creates nothing.
    /// </summary>
    public static void WeaponSlotsFireOclAfterCooldownWatchesTheSecondarySlot()
    {
        long Clouds(SimulationWorld world) => world.Entities.Count(value => value.Template.Name == "SlotCloud");

        void FireSlotWhenReady(SimulationWorld world, Feeder feeder, Entity source, Entity target, int slot)
        {
            for (var tick = 0; tick < 20; tick++)
            {
                if (feeder.FireWeaponSlot(1, source.Id.Value, target.Id.Value, slot).Accepted)
                {
                    world.Step();
                    return;
                }
                world.Step();
            }
            throw new InvalidOperationException("The slot never became ready.");
        }

        // The weapon cools down: two toxin shots, then nothing until the reload went by.
        var (world, feeder, fixture) = SlotIntegrationWorld(("SlotScud", 1, 0, 0), ("Dummy", 2, 50, 0));
        var scud = Slotted(world, "SlotScud");
        var dummy = Slotted(world, "Dummy");
        FireSlotWhenReady(world, feeder, scud, dummy, WeaponSlot.Secondary);
        FireSlotWhenReady(world, feeder, scud, dummy, WeaponSlot.Secondary);
        Check.That(scud.CurrentWeaponSlot == WeaponSlot.Secondary && scud.ClipOf(1) == 1, "The toxin slot is the current weapon with one round left.");
        for (var tick = 0; tick < 12; tick++) world.Step();
        Check.That(Clouds(world) == 1, $"After the series the cloud is created once the weapon could have fired again ({Clouds(world)}).");
        for (var tick = 0; tick < 40; tick++) world.Step();
        Check.That(Clouds(world) == 1, "Only one cloud per series.");

        // A single shot is no series.
        var (single, singleFeeder, _) = SlotIntegrationWorld(("SlotScud", 1, 0, 0), ("Dummy", 2, 50, 0));
        FireSlotWhenReady(single, singleFeeder, Slotted(single, "SlotScud"), Slotted(single, "Dummy"), WeaponSlot.Secondary);
        for (var tick = 0; tick < 60; tick++) single.Step();
        Check.That(Clouds(single) == 0, "One shot is below MinShotsToCreateOCL: no cloud.");

        // Switching to the other weapon in the middle of the series creates the cloud at once, long before the toxin slot reloads.
        var (switched, switchedFeeder, switchedFixture) = SlotIntegrationWorld(("SlotScud", 1, 0, 0), ("Dummy", 2, 50, 0));
        var other = Slotted(switched, "SlotScud");
        var victim = Slotted(switched, "Dummy");
        FireSlotWhenReady(switched, switchedFeeder, other, victim, WeaponSlot.Secondary);
        FireSlotWhenReady(switched, switchedFeeder, other, victim, WeaponSlot.Secondary);
        switched.Step(); // the watcher counts the second shot one tick after it
        Check.That(Clouds(switched) == 0, "Still no cloud while the series goes on.");
        Check.That(switched.Tick < Slotted(switched, "SlotScud").ReadyTickOf(1), "The toxin slot is still cooling down.");
        for (var tick = 0; tick < 3; tick++)
        {
            if (switchedFeeder.FireWeaponSlot(1, other.Id.Value, victim.Id.Value, WeaponSlot.Primary).Accepted) break;
            switched.Step();
        }
        switched.Step();
        switched.Step();
        Check.That(other.CurrentWeaponSlot == WeaponSlot.Primary && other.ReadyTickOf(1) > switched.Tick && Clouds(switched) == 1, $"Firing the primary gun ends the series at once ({Clouds(switched)} clouds, toxin ready at {other.ReadyTickOf(1)}, now {switched.Tick}).");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(switched), switchedFixture.Templates, switchedFixture.Fingerprint);
        for (var tick = 0; tick < 60; tick++) Check.That(switched.Step().StateHash == restored.Step().StateHash, $"The watcher continues identically after a restore (tick {tick}).");
        _ = fixture;
    }

    /// <summary>
    /// <c>AssistedTargetingUpdate</c> fires the real secondary slot: the clip, the reload and the last shot are the slot's own (not private bookkeeping of the module), and the slot is locked
    /// temporarily for the request as in the reference.
    /// </summary>
    public static void WeaponSlotsAssistedTargetingUsesTheRealSlot()
    {
        var world = CleanupWorld(out var feeder);
        var first = CsSpawn(world, feeder, "NetTurret", 1, 0, 0);
        var neighbour = CsSpawn(world, feeder, "NetTurret", 1, 60, 0);
        var victim = CsSpawn(world, feeder, "NetVictim", 2, 50, 80);
        Check.That(neighbour.ClipOf(1) == 2 && neighbour.ClipOf(0) == int.MaxValue, "The neighbour's secondary slot holds the assist weapon's clip of two.");
        Check.That(feeder.FireWeapon(1, first.Id.Value, victim.Id.Value).Accepted, "The first turret opens fire.");
        world.Step();
        Check.That(neighbour.WeaponLock == WeaponLockKind.Temporary && neighbour.CurrentWeaponSlot == WeaponSlot.Secondary, "The request pinned the assisting slot temporarily.");
        var shots = new List<long>();
        for (var tick = 0; tick < 60; tick++)
        {
            var before = neighbour.LastFireTickOf(WeaponSlot.Secondary);
            world.Step();
            if (neighbour.LastFireTickOf(WeaponSlot.Secondary) != before) shots.Add(neighbour.LastFireTickOf(WeaponSlot.Secondary));
        }
        Check.That(shots.Count == 3 && shots[1] - shots[0] == 3 && shots[2] - shots[1] == 15, $"Three assisting shots: the delay between shots, then the clip reload ({string.Join(",", shots)}).");
        Check.That(neighbour.ClipOf(1) == 1 && neighbour.ClipOf(0) == int.MaxValue, $"The slot's own clip was spent and reloaded ({neighbour.ClipOf(1)}).");
        Check.That(neighbour.LastFireTickOf(WeaponSlot.Primary) < 0, "The neighbour's primary weapon never fired.");
    }

    /// <summary>
    /// An aircraft with two weapons: the airfield reload covers every slot (<c>reloadAllAmmo</c>), and "out of special reload ammo" holds when every <c>RETURN_TO_BASE</c> weapon is empty
    /// whatever its other weapon still has (<c>JetAIUpdate::isOutOfSpecialReloadAmmo</c>).
    /// </summary>
    public static void WeaponSlotsJetReloadsEverySlotAtTheAirfield()
    {
        var files = new Dictionary<string, string>(AirFixtureFiles)
        {
            ["Data/INI/Object/AirSlotJet.ini"] = """
            Weapon AirSlotGun
              PrimaryDamage = 1.0
              AttackRange = 2000.0
              ClipSize = 0
              DelayBetweenShots = 100
            End
            Weapon AirSlotBombs
              PrimaryDamage = 20.0
              AttackRange = 2000.0
              ClipSize = 3
              ClipReloadTime = 2000
              DelayBetweenShots = 100
              AutoReloadsClip = RETURN_TO_BASE
            End
            Object AirSlotJet
              Side = Alpha
              KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE
              Locomotor = SET_NORMAL AirJetLoco
              Locomotor = SET_TAXIING AirTaxiLoco
              Body = ActiveBody Body01
                MaxHealth = 200.0
                InitialHealth = 200.0
              End
              WeaponSet
                Conditions = None
                Weapon = PRIMARY AirJetMissile
                Weapon = SECONDARY AirSlotGun
                Weapon = TERTIARY AirSlotBombs
              End
              Behavior = JetAIUpdate Jet01
                TakeoffPause = 300
                ReturnToBaseIdleTime = 1500
              End
              Behavior = DestroyDie Destroy01
              End
            End
            """,
        };
        var fixture = new SyntheticFixture(files: files);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 9, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "AirSlotJet", owner: 1, x: 500, y: 500);
        feeder.Spawn(2, "AirDummy", owner: 2, x: 600, y: 500);
        world.Step();
        var jet = Get(world, "AirSlotJet");
        var dummy = Get(world, "AirDummy");
        var ai = JetOf(jet);
        Check.That(jet.ClipOf(0) == 2 && jet.ClipOf(1) == int.MaxValue && jet.ClipOf(2) == 3, "The three slots hold their own clips.");
        Check.That(!ai.IsOutOfSpecialReloadAmmo(world), "Loaded: not out of special reload ammunition.");

        void FireWhenReady(int slot)
        {
            for (var tick = 0; tick < 10; tick++)
            {
                if (feeder.FireWeaponSlot(1, jet.Id.Value, dummy.Id.Value, slot).Accepted) { world.Step(); return; }
                world.Step();
            }
            throw new InvalidOperationException($"Slot {slot} never became ready.");
        }

        FireWhenReady(WeaponSlot.Primary);
        FireWhenReady(WeaponSlot.Primary);
        Check.That(jet.ClipOf(0) == 0 && jet.ReadyTickOf(0) >= CnCNew.Simulation.Time.SimulationTiming.SleepForever, "The emptied RETURN_TO_BASE slot never reloads by time.");
        Check.That(!ai.IsOutOfSpecialReloadAmmo(world), "One of two special weapons is still loaded: not out yet (the reference needs all of them empty).");
        FireWhenReady(WeaponSlot.Tertiary);
        FireWhenReady(WeaponSlot.Tertiary);
        FireWhenReady(WeaponSlot.Tertiary);
        Check.That(jet.ClipOf(2) == 0 && ai.IsOutOfSpecialReloadAmmo(world), "Both special weapons empty: out of special reload ammunition although the gun is loaded.");
        FireWhenReady(WeaponSlot.Secondary);

        world.ReloadAllAmmo(jet);
        Check.That(jet.ClipOf(0) == 2 && jet.ClipOf(2) == 3 && jet.ClipOf(1) == int.MaxValue && jet.ReadyTickOf(0) <= world.Tick && jet.ReadyTickOf(2) <= world.Tick, "The airfield reload fills every slot and makes them ready.");
        Check.That(!ai.IsOutOfSpecialReloadAmmo(world), "Reloaded.");
    }
}

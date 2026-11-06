using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-140: the attack machine's fire cycle (<c>AttackStateMachine</c> and <c>TurretStateMachine</c>, <c>AIStates.cpp</c>, <c>TurretAI.cpp</c>). After a shot the machine aims in the
/// next frame and fires in the one after, so a weapon fires on every second logic frame of the original at the most: the gap after a delay of D frames is the smallest even number
/// from max(D, 2) on, after a clip reload likewise; a contact weapon on the body does not aim and checks every frame; a wind-up is started when the fire state is entered, on the odd
/// frames. Every test runs at the rate of the run (<c>--tick-rate</c>) and gives the same frames at 30, 60 and 120 Hz.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> FireCycleFiles() => new Dictionary<string, string>(TurretAttackFiles())
    {
        ["Data/INI/Object/FireCycleFixture.ini"] = """
        Weapon CycleNineGun
          AcceptableAimDelta = 180
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon CycleTenGun
          AcceptableAimDelta = 180
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 333
        End
        Weapon CycleZeroGun
          AcceptableAimDelta = 180
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End
        Weapon CycleRifle
          AcceptableAimDelta = 180
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 3
          ClipReloadTime = 700
          DelayBetweenShots = 100
        End
        Weapon CycleCannon
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 2000
          DelayBetweenShots = 300
        End
        Weapon CycleKnife
          PrimaryDamage = 1
          AttackRange = 5.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 100
        End
        Weapon CycleWindupGun
          AcceptableAimDelta = 180
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 333
          PreAttackDelay = 166
          PreAttackType = PER_SHOT
        End
        Object CycleNineTank
          KindOf = SELECTABLE VEHICLE
          VisionRange = 200.0
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CycleNineGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object CycleNineSeeker
          KindOf = SELECTABLE VEHICLE
          VisionRange = 200.0
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CycleNineGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
        End
        Object CycleTenTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CycleTenGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object CycleZeroTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CycleZeroGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object CycleRifleTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CycleRifle
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object CycleCannonTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CycleCannon
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 270
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object CycleTurretTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CycleNineGun
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 270
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object CycleKnifeTank
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CycleKnife
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object CycleWindupTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CycleWindupGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>A world with <paramref name="template"/> of player 1 at the origin and one or two enemy runners of player 2 (north, then east).</summary>
    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, Entity Shooter, Entity North, Entity East) FireCycleWorld(string template, int distance = 100)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: FireCycleFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, template, owner: 1, x: 0, y: 0);
        feeder.Spawn(2, "RunnerTarget", owner: 2, x: 0, y: distance);
        feeder.Spawn(2, "RunnerTarget", owner: 2, x: distance, y: 0);
        // The first order comes with a logic frame of the original, at every rate.
        do world.Step(); while (!world.Time.IsOriginalFrameBoundary(world.Tick));
        return (world, feeder, fixture, Slotted(world, template), Slotted(world, "RunnerTarget"), Slotted(world, "RunnerTarget", 1));
    }

    /// <summary>
    /// Steps <paramref name="frames"/> logic frames of the original; <paramref name="order"/> is asked before every tick whether to give the fire order of that tick. The ticks of the
    /// shooter's shots in the order they fell.
    /// </summary>
    private static List<long> CycleShotTicks(SimulationWorld world, Feeder feeder, Entity shooter, Entity victim, int frames, Func<SimulationWorld, bool>? order)
    {
        var shots = new List<long>();
        for (var i = 0; i < world.Frames(frames); i++)
        {
            if (order?.Invoke(world) == true) feeder.FireWeapon(1, shooter.Id.Value, victim.Id.Value);
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == shooter.Id)) shots.Add(result.Tick);
        }
        return shots;
    }

    /// <summary>The shooter's own logic frame of the original that holds <paramref name="tick"/>, counted from its creation.</summary>
    private static long OwnFrame(SimulationWorld world, Entity shooter, long tick) => world.InFrames(tick - shooter.SpawnTick);

    /// <summary>The shots in the shooter's logic frames of the original after the first one.</summary>
    private static long[] FromFirst(SimulationWorld world, Entity shooter, List<long> shots) => [..shots.Select(tick => OwnFrame(world, shooter, tick) - OwnFrame(world, shooter, shots[0]))];

    /// <summary>A fire order every tick: the stream of an attack.</summary>
    private static bool EveryTick(SimulationWorld world) => true;

    public static void FireCycleDelaysFireOnEvenFrames()
    {
        foreach (var (template, name, frames, expected) in new (string, string, int, long[])[]
                 {
                     ("CycleNineTank", "CycleNineGun", 9, [0, 10, 20, 30]),
                     ("CycleTenTank", "CycleTenGun", 10, [0, 10, 20, 30]),
                     ("CycleZeroTank", "CycleZeroGun", 0, [0, 2, 4, 6, 8]),
                 })
        {
            var (world, feeder, fixture, shooter, north, _) = FireCycleWorld(template);
            Check.That(fixture.Templates.Weapons.TryGet(name, out var gun), name);
            // A delay of 0 ms is one frame here (a weapon fires once per frame); the cycle makes both two.
            Check.That(frames == 0 ? gun!.DelayBetweenShotsTicks <= world.Frames(1) : gun!.DelayBetweenShotsTicks == world.Frames(frames), $"{gun.Name}: the delay is {frames} frames ({world.InFrames(gun.DelayBetweenShotsTicks)}).");
            var shots = FromFirst(world, shooter, CycleShotTicks(world, feeder, shooter, north, 40, EveryTick));
            Check.That(shots.Take(expected.Length).SequenceEqual(expected),
                $"{template}: a delay of {frames} frames fires on the first even frame from max(D, 2) on (AIAttackFireWeaponState goes back to aiming after every shot): {string.Join(",", shots)}");
        }
    }

    public static void FireCycleClipReloadsOnTheCycle()
    {
        // RangerAdvancedCombatRifle: 3 frames between shots, a clip of 3, 21 frames of reload.
        var (world, feeder, _, rifle, north, _) = FireCycleWorld("CycleRifleTank");
        var shots = FromFirst(world, rifle, CycleShotTicks(world, feeder, rifle, north, 70, EveryTick));
        Check.That(shots.Take(7).SequenceEqual([0L, 4, 8, 30, 34, 38, 60]), "A clip of three, 3 frames apart and 21 to reload: every gap rounded up to the even frame: " + string.Join(",", shots));

        // OverlordTankGun: on a turret, 9 frames between shots, a clip of 2, 60 frames of reload.
        var (world2, feeder2, _, cannon, north2, _) = FireCycleWorld("CycleCannonTank");
        var turretShots = FromFirst(world2, cannon, CycleShotTicks(world2, feeder2, cannon, north2, 160, EveryTick));
        Check.That(turretShots.Take(5).SequenceEqual([0L, 10, 70, 80, 140]), "The turret's machine (TURRETAI_AIM, TURRETAI_FIRE) has the same cycle: " + string.Join(",", turretShots));
    }

    public static void FireCycleContactWeaponChecksEveryFrame()
    {
        var (world, feeder, _, knife, north, _) = FireCycleWorld("CycleKnifeTank", distance: 4);
        var shots = FromFirst(world, knife, CycleShotTicks(world, feeder, knife, north, 20, EveryTick));
        Check.That(shots.Take(4).SequenceEqual([0L, 3, 6, 9]), "A contact weapon on a body does not aim (its aim state succeeds as it is entered): it fires every max(D, 1) frames: " + string.Join(",", shots));
    }

    public static void FireCycleWindUpStartsWhenTheFireStateIsEntered()
    {
        var (world, feeder, fixture, tank, north, _) = FireCycleWorld("CycleWindupTank");
        Check.That(fixture.Templates.Weapons.TryGet("CycleWindupGun", out var gun) && gun.DelayBetweenShotsTicks == world.Frames(10) && gun.PreAttackDelayTicks == world.Frames(5),
            "The wind-up gun waits 10 frames and winds up 5.");
        var shots = CycleShotTicks(world, feeder, tank, north, 60, EveryTick);
        var gaps = FromFirst(world, tank, shots);
        // preFireCurrentWeapon winds up only when the weapon is ready by the next frame; the fire state is entered on the odd frames after a shot: the ninth, 5 frames of wind-up.
        Check.That(gaps.Length >= 4 && gaps.Take(4).SequenceEqual([0L, 14, 28, 42]), "PER_SHOT, 10 frames and 5 of wind-up: 14 frames apart: " + string.Join(",", gaps));
    }

    public static void FireCycleTurretThatTurnsAgainShiftsTheCycle()
    {
        var (world, feeder, _, cannon, north, east) = FireCycleWorld("CycleTurretTank");
        var shots = new List<long>();
        var victim = north;
        var transferred = -1L;
        var aligned = -1L;
        for (var i = 0; i < world.Frames(60) && shots.Count < 3; i++)
        {
            // After the first shot, at the end of its frame, the attack goes over to the runner in the east: the turret turns 90 degrees while the weapon reloads.
            if (shots.Count == 1 && transferred < 0 && world.Time.BeginsFrameOf(cannon, world.Tick))
            {
                world.TransferAttack(north.Id, east.Id);
                victim = east;
                transferred = world.Tick;
            }
            feeder.FireWeapon(1, cannon.Id.Value, victim.Id.Value);
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == cannon.Id)) shots.Add(result.Tick);
            // The first frame of the unit that begins with the turret pointing east (the bearing 0 relative to the unit facing 0): the turn of the frame before ended there.
            if (transferred >= 0 && aligned < 0 && world.Time.BeginsFrameOf(cannon, world.Tick) && cannon.TurretOf(0) is { } turret && turret.Angle.Abs() < CnCNew.Simulation.Numerics.Fixed.FromDecimal(0.035m))
                aligned = world.Tick;
        }
        Check.That(shots.Count == 3 && transferred >= 0 && aligned > transferred, $"Three shots, the turret turned: {string.Join(",", shots)} (transfer {transferred}, aligned {aligned}).");
        var turn = OwnFrame(world, cannon, aligned) - OwnFrame(world, cannon, transferred);
        Check.That(turn >= 9, $"The turn takes about ten frames ({turn}).");
        // The turret's aim state succeeds in the frame its turn ends; the fire state's update is the next frame, and the weapon has been ready since.
        Check.That(OwnFrame(world, cannon, shots[1]) == OwnFrame(world, cannon, aligned), $"The next shot comes the frame after the turret points again (frame {OwnFrame(world, cannon, shots[1])}, the turn ended before frame {OwnFrame(world, cannon, aligned)}).");
        Check.That(OwnFrame(world, cannon, shots[2]) - OwnFrame(world, cannon, shots[1]) == 10, $"From there the cycle runs on the even frames again: 9 frames between shots fire 10 apart ({OwnFrame(world, cannon, shots[2]) - OwnFrame(world, cannon, shots[1])}).");
    }

    public static void FireCycleOrderedAttackIsTheAcquiredOne()
    {
        // The unit's own attack.
        var (seekerWorld, seekerFeeder, _, seeker, seekerVictim, _) = FireCycleWorld("CycleNineSeeker");
        var acquired = CycleShotTicks(seekerWorld, seekerFeeder, seeker, seekerVictim, 50, null);
        Check.That(acquired.Count >= 4 && seekerWorld.AcquiredVictimOf(seeker).IsValid, $"The idle unit attacks by itself ({acquired.Count} shots).");

        // An attack order every tick, and the stream a command giver sends: an order in a tick that begins a frame, only while the weapon could fire (StandingOrders).
        var (world, feeder, _, tank, north, _) = FireCycleWorld("CycleNineTank");
        var firstOrder = world.Tick;
        var stream = CycleShotTicks(world, feeder, tank, north, 50, EveryTick);
        var (world2, feeder2, _, tank2, north2, _) = FireCycleWorld("CycleNineTank");
        var client = CycleShotTicks(world2, feeder2, tank2, north2, 50,
            w => w.Time.IsOriginalFrameBoundary(w.Tick) && w.ChooseWeapon(tank2, north2, FireOrigin.Player).CanFire);
        // One order only: one shot, in the frame the attack's machine fires it.
        var (world3, feeder3, _, tank3, north3, _) = FireCycleWorld("CycleNineTank");
        var once = CycleShotTicks(world3, feeder3, tank3, north3, 50, w => w.Tick == firstOrder);

        Check.That(OwnFrame(world, tank, stream[0]) - OwnFrame(world, tank, firstOrder) == 1, $"An order's attack aims in the order's frame and fires in the next ({OwnFrame(world, tank, stream[0]) - OwnFrame(world, tank, firstOrder)} frames).");
        var reference = FromFirst(seekerWorld, seeker, acquired);
        foreach (var (name, shots, w, shooter) in new[] { ("every tick", stream, world, tank), ("as a command giver sends", client, world2, tank2) })
        {
            var gaps = FromFirst(w, shooter, shots);
            Check.That(gaps.Length >= 4 && gaps.Take(4).SequenceEqual(reference.Take(4)) && gaps.Take(4).SequenceEqual([0L, 10, 20, 30]),
                $"The ordered attack ({name}) fires on the frames of the unit's own: {string.Join(",", gaps)} against {string.Join(",", reference)}.");
        }
        Check.That(stream.SequenceEqual(client), $"The orders between the shots change nothing: {string.Join(",", stream)} / {string.Join(",", client)}.");
        Check.That(once.Count == 1 && once[0] == stream[0], $"One order is one shot, on the machine's frame: {string.Join(",", once)}.");
    }

    public static void FireCycleSnapshotBetweenShotsContinues()
    {
        foreach (var (template, cycle) in new[] { ("CycleNineTank", 10L), ("CycleTurretTank", 10L), ("CycleWindupTank", 14L) })
        {
            var (world, feeder, fixture, tank, north, _) = FireCycleWorld(template);
            var shots = CycleShotTicks(world, feeder, tank, north, 1, EveryTick);
            // Into the cycle: after the first shot, then a few ticks more - at 60 and 120 Hz also into the middle of a frame.
            while (shots.Count < 1) shots.AddRange(CycleShotTicks(world, feeder, tank, north, 1, EveryTick));
            for (var i = 0; i < world.Frames(3) + 1; i++)
            {
                feeder.FireWeapon(1, tank.Id.Value, north.Id.Value);
                if (world.Step().Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id)) shots.Add(world.Tick - 1);
            }
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
            var fork = feeder.ForkTo(restored);
            Check.That(restored.StateHash == world.StateHash, $"{template}: the restored world is the same.");
            var tankThere = restored.FindEntity(tank.Id)!;
            var later = new List<long>();
            var laterThere = new List<long>();
            for (var i = 0; i < world.Frames(40); i++)
            {
                feeder.FireWeapon(1, tank.Id.Value, north.Id.Value);
                fork.FireWeapon(1, tank.Id.Value, north.Id.Value);
                var here = world.Step();
                var there = restored.Step();
                Check.That(here.StateHash == there.StateHash, $"{template}: the restored world fires identically at step {i}.");
                if (here.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id)) later.Add(here.Tick);
                if (there.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tankThere.Id)) laterThere.Add(there.Tick);
            }
            Check.That(later.Count >= 2 && later.SequenceEqual(laterThere), $"{template}: the same shots: {string.Join(",", later)} / {string.Join(",", laterThere)}.");
            var gaps = FromFirst(world, tank, later);
            Check.That(gaps.Zip(gaps.Skip(1), (a, b) => b - a).All(gap => gap == cycle), $"{template}: on the cycle, {cycle} frames apart: {string.Join(",", gaps)}.");
        }
    }
}

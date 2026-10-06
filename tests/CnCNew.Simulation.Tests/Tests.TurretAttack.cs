using System.Collections.Immutable;
using System.Text;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The persistent attack state and the idle scan of the turret model (docs/SIM-TURRET-AIM.md §3a/§3b): an object that was ordered to attack keeps its turrets on the victim until the victim is
/// gone, the unit gets another order or loses interest — through reloads, gaps in the order stream and a chase; an idle turret whose data say so glances around at random.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> TurretAttackFiles() => new Dictionary<string, string>(TurretFiles())
    {
        ["Data/INI/Object/TurretAttackFixture.ini"] = """
        Locomotor ChaseLoco
          Speed = 90.0
          Acceleration = 300.0
          Braking = 300.0
          TurnRate = 360.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Object ChaseTank
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TurretGun
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 180
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object RunnerTarget
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object FragileRunner
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ScanTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TurretGun
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 180
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
              MinIdleScanInterval = 1000
              MaxIdleScanInterval = 2000
              MinIdleScanAngle = 30
              MaxIdleScanAngle = 90
            End
          End
        End
        Object FogSpotter
          KindOf = SELECTABLE VEHICLE
          VisionRange = 40.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        SpecialPower SPECIAL_TurretAbility
          Enum = SPECIAL_BLACKLOTUS_STEAL_CASH_HACK
          ReloadTime = 0
        End
        Object AbilityTank
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TurretGun
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 180
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
          Behavior = SpecialAbility Ability01
            SpecialPowerTemplate = SPECIAL_TurretAbility
          End
          Behavior = SpecialAbilityUpdate AbilityUpdate01
            SpecialPowerTemplate = SPECIAL_TurretAbility
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) AttackWorld(uint seed = 1, params (string Template, int Owner, int X, int Y, decimal Heading)[] objects)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: TurretAttackFiles());
        var world = NewWorld(fixture, seed);
        var feeder = new Feeder(world);
        foreach (var (template, owner, x, y, heading) in objects) feeder.Spawn(owner, template, owner: owner, x: x, y: y, angle: Fixed.FromDecimal(heading));
        world.Step();
        return (world, feeder, fixture);
    }

    private static decimal DegreesOf(Fixed radians) => radians.ToDecimal() * 180m / 3.14159265358979m;

    /// <summary>How far the turret's world direction is off the bearing to the victim, in degrees.</summary>
    private static decimal AimError(Entity shooter, Entity victim, int turret = 0)
        => DegreesOf(FixedTrig.AngleDifference(shooter.Angle + shooter.TurretOf(turret)!.Value.Angle, FixedTrig.Atan2(victim.Y - shooter.Y, victim.X - shooter.X)).Abs());

    private static decimal DistanceBetween(Entity a, Entity b)
    {
        var dx = (a.X - b.X).ToDecimal();
        var dy = (a.Y - b.Y).ToDecimal();
        return (decimal)Math.Sqrt((double)(dx * dx + dy * dy));
    }

    public static void TurretAttackSurvivesGapsChaseAndAnOrderEndsIt()
    {
        // A tank drives past its victim (the body does not face it) and the turret has to follow the whole way; the order stream then breaks off for a long time.
        var (world, feeder, _) = AttackWorld(1, ("ChaseTank", 1, 0, 0, 0m), ("RunnerTarget", 2, 200, 300, 0m));
        var tank = Slotted(world, "ChaseTank");
        var runner = Slotted(world, "RunnerTarget");
        Check.That(!world.AttackTargetOf(tank).IsValid && world.HasAimingTurret(tank), "Before any order the tank attacks nothing.");
        Check.That(feeder.Pursue(1, tank.Id.Value, runner.Id.Value, 200, 200).Accepted, "A pursuit order is accepted while the victim is out of weapon range.");
        var worst = 0m;
        var shots = 0;
        var arrived = -1;
        for (var step = 0; step < world.Frames(200) && arrived < 0; step++)
        {
            var result = world.Step();
            shots += result.Events.Count(value => value.Kind == SimEventKind.WeaponFired);
            if (step >= world.Frames(10)) worst = Math.Max(worst, AimError(tank, runner));
            if (!tank.HasMoveTarget) arrived = step;
        }

        Check.That(arrived > world.Frames(20) && world.AttackTargetOf(tank) == runner.Id, $"The tank drove {arrived} ticks and still attacks the victim.");
        Check.That(worst <= 2.5m && shots == 0, $"Its turret stayed on the victim from tick 10 on while it drove past ({worst:F2} degrees worst); a pursuit fires nothing.");
        var distance = DistanceBetween(tank, runner);
        Check.That(distance < 150m, $"It ends within weapon range ({distance:F0}).");

        // Fire orders: no order waits, because the turret is already there.
        var waitsBefore = world.TurretWaits;
        for (var i = 0; i < world.Frames(40); i++)
        {
            feeder.FireWeapon(1, tank.Id.Value, runner.Id.Value);
            shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired);
        }

        Check.That(shots >= 3 && world.TurretWaits == waitsBefore, $"The first shot goes out at once: {shots} shots, {world.TurretWaits - waitsBefore} orders waited for the turret.");

        // The order stream breaks off for ten seconds (the weapon reloads, nobody asks): the turret does not turn back as long as the victim lives.
        worst = 0m;
        for (var i = 0; i < world.Frames(300); i++)
        {
            world.Step();
            worst = Math.Max(worst, AimError(tank, runner));
        }

        Check.That(worst <= 2m && world.AttackTargetOf(tank) == runner.Id, $"300 ticks without an order: the turret still points at the victim ({worst:F2} degrees) and the attack stands.");

        // The victim walks (in range): the turret follows it.
        feeder.SetMoveTarget(2, runner.Id.Value, 280, 280);
        worst = 0m;
        for (var i = 0; i < world.Frames(40); i++)
        {
            world.Step();
            worst = Math.Max(worst, AimError(tank, runner));
        }

        Check.That(worst <= 3m, $"A walking victim is tracked ({worst:F2} degrees worst).");

        // A plain move order is another AI command: the attack ends, the turret holds its angle for RecenterTime (15 ticks) and then turns back at half speed.
        var held = tank.TurretOf(0)!.Value.Angle;
        Check.That(held.Abs() > Degrees(20m), "The turret is well away from its natural angle when the order comes.");
        feeder.SetMoveTarget(1, tank.Id.Value, 200, 100);
        for (var i = 0; i < 1 + world.Frames(15); i++)
        {
            world.Step();
            Check.That(tank.TurretOf(0)!.Value.Angle == held && !tank.TurretOf(0)!.Value.Rotating, $"Hold tick {i}: the angle stays for the recentre time.");
        }

        Check.That(!world.AttackTargetOf(tank).IsValid, "The attack is over.");
        world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle != held && tank.TurretOf(0)!.Value.Rotating, "Then it starts to turn back.");
        for (var i = 0; i < world.Frames(90); i++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle == Fixed.Zero, "...and rests at the natural angle.");
        Check.That(!Encoding.UTF8.GetString(WorldSnapshot.Write(world)).Contains("\"persistent\""), "No attack state is left in the snapshot.");
    }

    public static void TurretAttackEndsWhenTheVictimDiesOrIsLost()
    {
        // A victim that dies: the turret holds for RecenterTime, then recentres.
        var (world, feeder, _) = AttackWorld(1, ("TurretTank", 1, 0, 0, 0m), ("FragileTarget", 2, 0, 100, 0m));
        var tank = Slotted(world, "TurretTank");
        var fragile = IdOf(world, "FragileTarget");
        Check.That(TicksUntilShot(world, feeder, tank.Id.Value, fragile) == world.Frames(15), "The turret turned toward the target and fired.");
        for (var i = 0; i < world.Frames(200); i++) world.Step();
        Check.That(Near(tank.TurretOf(0)!.Value.Angle, Degrees(90m), 1m) && !tank.TurretOf(0)!.Value.Rotating && world.AttackTargetOf(tank).Value == fragile,
            "200 ticks after the last order the turret still points at the victim: the attack goes on.");
        Check.That(Encoding.UTF8.GetString(WorldSnapshot.Write(world)).Contains("\"persistent\""), "The attack is part of the snapshot.");

        feeder.Kill(fragile);
        for (var i = 0; i < 1 + world.Frames(15); i++)
        {
            world.Step();
            Check.That(Near(tank.TurretOf(0)!.Value.Angle, Degrees(90m), 1m) && !tank.TurretOf(0)!.Value.Rotating, $"The victim is dead; hold tick {i}.");
        }

        Check.That(!world.AttackTargetOf(tank).IsValid, "A dead victim ends the attack.");
        world.Step();
        Check.That(tank.TurretOf(0)!.Value.Rotating && tank.TurretOf(0)!.Value.Angle < Degrees(90m), "After the hold it recentres...");
        for (var i = 0; i < world.Frames(40); i++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle == Fixed.Zero && !Encoding.UTF8.GetString(WorldSnapshot.Write(world)).Contains("\"turrets\""), "...at half speed (90 degrees at 3 per tick) and rests.");

        // A victim that outruns a tank that stands still: out of reach of every weapon, the tank loses interest after 45 ticks.
        var (chase, chaseFeeder, _) = AttackWorld(1, ("TurretTank", 1, 0, 0, 0m), ("RunnerTarget", 2, 0, 100, 0m));
        var watcher = Slotted(chase, "TurretTank");
        var runner = Slotted(chase, "RunnerTarget");
        TicksUntilShot(chase, chaseFeeder, watcher.Id.Value, runner.Id.Value);
        chaseFeeder.SetMoveTarget(2, runner.Id.Value, 0, 400);
        long outOfRange = -1;
        long lost = -1;
        var worst = 0m;
        for (var i = 0; i < chase.Frames(300) && lost < 0; i++)
        {
            chase.Step();
            if (outOfRange < 0 && DistanceBetween(watcher, runner) > 150m) outOfRange = i;
            if (!chase.AttackTargetOf(watcher).IsValid) lost = i;
            else if (i > chase.Frames(5)) worst = Math.Max(worst, AimError(watcher, runner));
        }

        Check.That(outOfRange > 0 && chase.InFrames(lost - outOfRange) is >= 44 and <= 47, $"The victim left weapon range at step {outOfRange}; the tank gave up at step {lost}.");
        Check.That(worst <= 2m, $"It kept its turret on the departing victim until then ({worst:F2} degrees).");

        // An attack that began with a fire order is given up when the victim stays out of reach, whether the unit stands still or walks on (attack-move, an AI that marches to its goal)...
        var (walking, walkingFeeder, _) = AttackWorld(1, ("ChaseTank", 1, 0, 0, 0m), ("RunnerTarget", 2, 0, 100, 0m));
        var walker = Slotted(walking, "ChaseTank");
        var bystander = Slotted(walking, "RunnerTarget");
        walkingFeeder.SetMoveTarget(1, walker.Id.Value, -600, 0);
        walking.Step();
        walkingFeeder.FireWeapon(1, walker.Id.Value, bystander.Id.Value);
        long left = -1;
        long gaveUp = -1;
        for (var i = 0; i < walking.Frames(300) && gaveUp < 0; i++)
        {
            walking.Step();
            if (left < 0 && DistanceBetween(walker, bystander) > 150m) left = i;
            if (!walking.AttackTargetOf(walker).IsValid) gaveUp = i;
        }

        Check.That(left > 0 && walker.HasMoveTarget && walking.InFrames(gaveUp - left) is >= 44 and <= 47, $"A walking unit that has fired at a victim now out of reach gives up after 45 ticks (out of reach at {left}, given up at {gaveUp}, still walking {walker.HasMoveTarget}).");

        // ...but a unit that pursues its victim never gives up on distance: it is on its way.
        var (pursuer, pursuerFeeder, _) = AttackWorld(1, ("ChaseTank", 1, 0, 0, 0m), ("RunnerTarget", 2, 0, 400, 0m));
        var hunter = Slotted(pursuer, "ChaseTank");
        var prey = Slotted(pursuer, "RunnerTarget");
        pursuerFeeder.Pursue(1, hunter.Id.Value, prey.Id.Value, 0, 0);
        for (var i = 0; i < pursuer.Frames(300); i++) pursuer.Step();
        Check.That(pursuer.AttackTargetOf(hunter) == prey.Id && DistanceBetween(hunter, prey) > 150m, "A pursuit stands while the victim is far away, even when the unit waits.");
        Check.That(Encoding.UTF8.GetString(WorldSnapshot.Write(pursuer)).Contains("\"pursuing\""), "The pursuit is part of the snapshot.");

        // The attacker dies: nothing is left behind and the world goes on.
        var (dying, dyingFeeder, fixture) = AttackWorld(1, ("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var dyingTank = Slotted(dying, "TurretTank");
        TicksUntilShot(dying, dyingFeeder, dyingTank.Id.Value, IdOf(dying, "TurretTarget"));
        dyingFeeder.Kill(dyingTank.Id.Value);
        for (var i = 0; i < dying.Frames(40); i++) dying.Step();
        Check.That(dying.FindEntity(dyingTank.Id) is null || dyingTank.EffectivelyDead, "The attacker died.");
        var bytes = WorldSnapshot.Write(dying);
        Check.That(WorldSnapshot.Write(WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint)).AsSpan().SequenceEqual(bytes), "The world with the dead attacker round-trips.");
    }

    public static void TurretAttackOnlyFromAnAttackOrder()
    {
        // A script's one-off command button is not an attack: the turret turns, holds for the usual grace time and turns back.
        var (script, scriptFeeder, _) = AttackWorld(1, ("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var scripted = Slotted(script, "TurretTank");
        TicksUntilShot(script, scriptFeeder, scripted.Id.Value, IdOf(script, "TurretTarget"), origin: FireOrigin.Script);
        Check.That(!script.AttackTargetOf(scripted).IsValid, "A script's one-off shot does not start an attack...");
        for (var i = 0; i < script.Frames(120); i++) script.Step();
        Check.That(scripted.TurretOf(0)!.Value.Angle == Fixed.Zero, "...so the turret is back at its natural angle long before 120 ticks have passed.");

        // A new fire order at another victim replaces the old one.
        var (world, feeder, _) = AttackWorld(1, ("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m), ("TurretTarget", 2, 100, 0, 0m));
        var tank = Slotted(world, "TurretTank");
        var north = IdOf(world, "TurretTarget", 0);
        var east = IdOf(world, "TurretTarget", 1);
        TicksUntilShot(world, feeder, tank.Id.Value, north);
        Check.That(world.AttackTargetOf(tank).Value == north, "The attack names its victim.");
        TicksUntilShot(world, feeder, tank.Id.Value, east);
        Check.That(world.AttackTargetOf(tank).Value == east && Near(tank.TurretOf(0)!.Value.Angle, Fixed.Zero, 1m), "A fire order at another victim moves the attack, and the turret follows.");

        // A pursuit needs a victim that exists; one that no turret can aim at (no turret, no weapon that hurts it) only moves the unit.
        Check.That(feeder.Pursue(1, tank.Id.Value, north, 0, 0).Accepted, "A pursuit is accepted...");
        world.Step();
        Check.That(world.AttackTargetOf(tank).Value == north, "...and starts the attack on its victim although nothing has been fired.");
        Check.That(!feeder.Pursue(1, tank.Id.Value, 99999, 10, 10).Accepted, "An unknown victim is refused.");
    }

    public static void TurretAttackEndsWhenTheVictimIsNoLongerVisible()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: TurretAttackFiles());
        var terrain = NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        world.RevealMapPermanently(new PlayerId(1), true);
        feeder.Spawn(1, "TurretTank", owner: 1, x: 20, y: 20);
        feeder.Spawn(2, "TurretTarget", owner: 2, x: 20, y: 100);
        world.Step();
        var tank = Slotted(world, "TurretTank");
        var target = Slotted(world, "TurretTarget");
        Check.That(TicksUntilShot(world, feeder, tank.Id.Value, target.Id.Value) >= 0 && world.AttackTargetOf(tank) == target.Id, "The attack on a visible victim stands.");
        for (var i = 0; i < world.Frames(30); i++) world.Step();
        Check.That(world.AttackTargetOf(tank) == target.Id, "...as long as the victim stays visible.");
        world.SetStatus(target.Id, 1UL << ObjectStatusBits.Index("STEALTHED"));
        world.Step();
        Check.That(!world.AttackTargetOf(tank).IsValid, "A victim that is no longer attackable for its attacker's player (here: stealthed) ends the aim.");
        for (var i = 0; i < world.Frames(60); i++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle == Fixed.Zero, "The turret recentres after the hold.");
    }

    /// <summary>The fog half of the visibility rule: the victim stays in weapon range and unstealthed, but the only object that saw it is gone.</summary>
    public static void TurretAttackEndsWhenTheVictimFallsIntoTheFog()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: TurretAttackFiles());
        var terrain = NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "TurretTank", owner: 1, x: 20, y: 20);
        feeder.Spawn(2, "TurretTarget", owner: 2, x: 20, y: 100);
        feeder.Spawn(1, "FogSpotter", owner: 1, x: 30, y: 110);
        world.Step();
        world.Step();
        var tank = Slotted(world, "TurretTank");
        var target = Slotted(world, "TurretTarget");
        var player = new PlayerId(1);
        Check.That(world.CanPlayerSee(player, target), "The spotter shows the victim to its player; the tank itself sees nothing.");
        Check.That(TicksUntilShot(world, feeder, tank.Id.Value, target.Id.Value) >= 0 && world.AttackTargetOf(tank) == target.Id, "The attack on a victim another object sees stands.");
        for (var i = 0; i < world.Frames(30); i++) world.Step();
        Check.That(world.AttackTargetOf(tank) == target.Id, "...as long as the spotter lives (the victim is in range, so nothing else ends the attack).");

        feeder.Destroy(IdOf(world, "FogSpotter"));
        var ended = -1;
        for (var i = 0; i < world.Frames(10) && ended < 0; i++)
        {
            world.Step();
            if (!world.AttackTargetOf(tank).IsValid) ended = i;
        }
        Check.That(!world.CanPlayerSee(player, target), "Without the spotter the victim is in the fog.");
        Check.That(ended >= 0, "A victim in the fog ends the aim although it is in range and not stealthed.");
        Check.That(!feeder.FireWeapon(1, tank.Id.Value, target.Id.Value).Accepted || world.Step().Events.All(value => value.Kind != SimEventKind.WeaponFired), "No shot at a victim in the fog.");
        Check.That(!world.AttackTargetOf(tank).IsValid, "...and a refused fire order does not start the attack again.");
        for (var i = 0; i < world.Frames(80); i++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle == Fixed.Zero, "The turret recentres after the hold.");
    }

    /// <summary><c>SpecialAbilityUpdate::initiateIntentToDoSpecialPower</c> idles the unit: its move and its attack end when it starts a unit ability.</summary>
    public static void TurretAttackEndsWhenTheUnitStartsAnAbility()
    {
        var (world, feeder, _) = AttackWorld(1, ("AbilityTank", 1, 0, 0, 0m), ("RunnerTarget", 2, 0, 100, 0m));
        var tank = Slotted(world, "AbilityTank");
        var target = IdOf(world, "RunnerTarget");
        feeder.Pursue(1, tank.Id.Value, target, 300, 0);
        world.Step();
        world.Step();
        Check.That(world.AttackTargetOf(tank).Value == target && tank.HasMoveTarget, "The tank pursues its victim.");
        Check.That(feeder.UseSpecialPower(1, tank.Id.Value, "SPECIAL_TurretAbility").Accepted, "The ability is accepted.");
        world.Step();
        Check.That(!world.AttackTargetOf(tank).IsValid, "Starting a unit ability ends the unit's attack...");
        Check.That(!tank.HasMoveTarget, "...and its move.");
    }

    public static void TurretAttackSnapshotAndReplay()
    {
        var (first, firstFeeder, fixture) = AttackWorld(3, ("ChaseTank", 1, 0, 0, 0m), ("RunnerTarget", 2, 200, 300, 0m), ("ScanTank", 1, -300, 0, 0m));
        var (second, secondFeeder, _) = AttackWorld(3, ("ChaseTank", 1, 0, 0, 0m), ("RunnerTarget", 2, 200, 300, 0m), ("ScanTank", 1, -300, 0, 0m));
        List<ulong> Run(SimulationWorld world, Feeder feeder, int frames)
        {
            var hashes = new List<ulong>();
            for (var i = 0; i < world.Frames(frames); i++)
            {
                var tank = IdOf(world, "ChaseTank");
                var runner = IdOf(world, "RunnerTarget");
                if (world.Tick == world.Frames(3)) feeder.Pursue(1, tank, runner, 200, 200);
                if (world.InFrames(world.Tick) is >= 60 and < 90) feeder.FireWeapon(1, tank, runner);
                if (world.Tick == world.Frames(150)) feeder.SetMoveTarget(2, runner, 260, 280);
                if (world.Tick == world.Frames(260)) feeder.SetMoveTarget(1, tank, 100, 100);
                hashes.Add(world.Step().StateHash);
            }

            return hashes;
        }

        Check.That(Run(first, firstFeeder, 40).SequenceEqual(Run(second, secondFeeder, 40)), "Two independent runs agree while the tank pursues and the other one scans.");
        var bytes = WorldSnapshot.Write(first);
        var text = Encoding.UTF8.GetString(bytes);
        Check.That(text.Contains("\"persistent\"") && text.Contains("\"nextScan\""), "The snapshot is taken mid-pursuit and carries both the attack and the scan timer.");
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "The snapshot round-trips byte-identically.");
        var restoredFeeder = firstFeeder.ForkTo(restored);
        var a = Run(first, firstFeeder, 400);
        var b = Run(restored, restoredFeeder, 400);
        var c = Run(second, secondFeeder, 400);
        Check.That(a.SequenceEqual(b) && a.SequenceEqual(c), "A world restored mid-attack continues exactly like the live one, and like an independent run.");

        // The pursuit survives the replay file; the replay reproduces the run.
        var player1 = new PlayerId(1);
        var commands = new List<SimCommand>
        {
            new SpawnCommand(player1, 1, 0, "ChaseTank", player1, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero),
            new SpawnCommand(new PlayerId(2), 1, 0, "RunnerTarget", new PlayerId(2), Fixed.FromInt(200), Fixed.FromInt(300), Fixed.Zero, Fixed.Zero),
            new PursueCommand(player1, 2, 3, new EntityId(1), new EntityId(2), Fixed.FromInt(200), Fixed.FromInt(200)),
        };
        ulong sequence = 2;
        for (var tick = 70; tick < 110; tick++) commands.Add(new FireWeaponCommand(player1, ++sequence, tick, new EntityId(1), new EntityId(2)));
        commands.Add(new SetMoveTargetCommand(player1, ++sequence, 130, new EntityId(1), Fixed.FromInt(100), Fixed.FromInt(100)));
        var ordered = commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence).ToImmutableArray();
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, fixture.Fingerprint, SyntheticFixture.FixtureId, 1, new WorldId(1), TwoPlayers, 200, 20);
        var probe = ReplayRunner.Run(new ReplayFile(header, ordered, []), fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        var file = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, ordered, probe.Checkpoints)));
        Check.That(file.Commands.SequenceEqual(ordered), "PursueCommand survives the replay file.");
        var again = ReplayRunner.Run(file, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(again.TickHashes.AsSpan().SequenceEqual(probe.TickHashes) && again.VerifiedCheckpoints == file.Checkpoints.Length, "The replay reproduces itself tick by tick.");
        Check.That(CommandTypes.IsKnown(CommandTypes.Pursue) && CommandTypes.IsKnown(CommandTypes.AttackMove) && !CommandTypes.IsKnown(CommandTypes.AttackMove + 1) && CommandTypes.Version == 14 && ReplayFormat.CurrentVersion == 11, "Command table 14 and replay format 11 carry the pursuit (and the attack-move after it).");
        Check.That(ordered.OfType<PursueCommand>().Single().TypeCode == CommandTypes.Pursue, "The command has its own type code.");
    }

    public static void TurretIdleScanGlancesAroundDeterministically()
    {
        // ScanTank: 1000..2000 ms between scans (30..60 ticks), 30..90 degrees off the natural angle, RecenterTime 500 ms (15 ticks), turret 180 degrees per second (3 per tick at half speed).
        var (world, _, fixture) = AttackWorld(1, ("ScanTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 400, 0m));
        var tank = Slotted(world, "ScanTank");
        Check.That(Template(fixture.Templates, "ScanTank").Turrets![0]!.CanScan && !Template(fixture.Templates, "TurretTank").Turrets![0]!.CanScan, "A turret with a scan angle and a real interval scans; one without does not.");
        var angles = new List<Fixed>();
        var hashes = new List<ulong>();
        for (var i = 0; i < world.Frames(1500); i++)
        {
            hashes.Add(world.Step().StateHash);
            angles.Add(tank.TurretOf(0)!.Value.Angle);
        }

        // Cut the series into excursions: the stretch of ticks from the first step away from the natural angle to the return to it.
        var excursions = new List<(int Start, int End, Fixed Peak, int AtPeak)>();
        var rests = new List<(int From, int To)>();
        var index = 0;
        while (index < angles.Count)
        {
            if (angles[index] != Fixed.Zero)
            {
                var start = index;
                var peak = Fixed.Zero;
                while (index < angles.Count && angles[index] != Fixed.Zero)
                {
                    if (angles[index].Abs() > peak.Abs()) peak = angles[index];
                    index++;
                }

                var atPeak = angles.Skip(start).Take(index - start).Count(value => value == peak);
                excursions.Add((start, index, peak, atPeak));
            }
            else index++;
        }

        Check.That(excursions.Count >= 8, $"Over 1500 ticks the turret scanned {excursions.Count} times.");
        Check.That(excursions.All(value => value.Peak.Abs() >= Degrees(29.9m) && value.Peak.Abs() <= Degrees(90.1m)), "Every scan angle lies between MinIdleScanAngle and MaxIdleScanAngle off the natural angle: " + string.Join(", ", excursions.Select(value => DegreesOf(value.Peak).ToString("F0"))) + ".");
        Check.That(excursions.Any(value => value.Peak.IsNegative) && excursions.Any(value => !value.Peak.IsNegative), "Both sides occur.");
        Check.That(excursions.Select(value => value.Peak).Distinct().Count() >= excursions.Count - 1, "The angles are drawn, not repeated.");
        Check.That(excursions.All(value => world.InFrames(value.AtPeak) is >= 15 and <= 17), "It holds each scan angle for RecenterTime (15 ticks): " + string.Join(", ", excursions.Select(value => value.AtPeak)) + " ticks.");
        var gaps = excursions.Zip(excursions.Skip(1), (left, right) => right.Start - left.End).ToArray();
        Check.That(gaps.All(gap => world.InFrames(gap) is >= 30 and <= 60), "The next scan starts 30..60 ticks (1000..2000 ms) after the turret came to rest: " + string.Join(", ", gaps) + ".");
        Check.That(gaps.Distinct().Count() > 1, "The interval is random, not constant.");
        Check.That(excursions.All(value => value.End - value.Start <= world.Frames(31 + 16 + 31)), "A scan and the return take their time at half the turn rate.");

        // The same world again: identical, and the shared logic stream was not touched.
        var (twin, _, _) = AttackWorld(1, ("ScanTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 400, 0m));
        var twinHashes = new List<ulong>();
        for (var i = 0; i < twin.Frames(1500); i++) twinHashes.Add(twin.Step().StateHash);
        Check.That(twinHashes.SequenceEqual(hashes), "Two independent runs scan identically.");
        var (quiet, _, _) = AttackWorld(1, ("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 400, 0m));
        for (var i = 0; i < quiet.Frames(1500); i++) quiet.Step();
        Check.That(world.LogicRandom.NextUInt32() == quiet.LogicRandom.NextUInt32(), "Scanning draws nothing from the shared logic stream (a world without a scanning turret has the same state).");
        var (other, _, _) = AttackWorld(2, ("ScanTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 400, 0m));
        var otherAngles = new List<Fixed>();
        for (var i = 0; i < other.Frames(400); i++) { other.Step(); otherAngles.Add(Slotted(other, "ScanTank").TurretOf(0)!.Value.Angle); }
        Check.That(!otherAngles.SequenceEqual(angles.Take(400)), "Another world seed scans differently.");

        // Snapshot in the middle of a scan.
        var (live, _, _) = AttackWorld(1, ("ScanTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 400, 0m));
        var liveTank = Slotted(live, "ScanTank");
        var guard = 0;
        while (liveTank.TurretOf(0)!.Value.Angle == Fixed.Zero && guard++ < 400) live.Step();
        live.Step();
        var bytes = WorldSnapshot.Write(live);
        Check.That(Encoding.UTF8.GetString(bytes).Contains("\"scanning\"") && Encoding.UTF8.GetString(bytes).Contains("\"scanDraws\""), "The snapshot is taken mid-scan.");
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "The mid-scan snapshot round-trips byte-identically.");
        var rest = new List<ulong>();
        var restoredRest = new List<ulong>();
        for (var i = 0; i < live.Frames(600); i++) { rest.Add(live.Step().StateHash); restoredRest.Add(restored.Step().StateHash); }
        Check.That(rest.SequenceEqual(restoredRest), "A world restored mid-scan scans on exactly like the live one.");
    }

    public static void TurretIdleScanYieldsToAnAttackAndResumes()
    {
        var (world, feeder, _) = AttackWorld(1, ("ScanTank", 1, 0, 0, 0m), ("FragileTarget", 2, 0, 100, 0m));
        var tank = Slotted(world, "ScanTank");
        var fragile = IdOf(world, "FragileTarget");
        for (var i = 0; i < world.Frames(100); i++) world.Step();
        TicksUntilShot(world, feeder, tank.Id.Value, fragile);
        for (var i = 0; i < world.Frames(150); i++)
        {
            world.Step();
            Check.That(Near(tank.TurretOf(0)!.Value.Angle, Degrees(90m), 1m), "An attacking turret does not glance around.");
        }

        feeder.Kill(fragile);
        var moved = false;
        for (var i = 0; i < world.Frames(400) && !moved; i++)
        {
            world.Step();
            if (i > world.Frames(140) && tank.TurretOf(0)!.Value.Angle.Abs() > Degrees(1m) && tank.TurretOf(0)!.Value.Angle.Abs() < Degrees(89m) && !tank.TurretOf(0)!.Value.Rotating) moved = true;
        }

        for (var i = 0; i < world.Frames(300) && tank.TurretOf(0)!.Value.Angle != Fixed.Zero; i++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle == Fixed.Zero, "After the victim died the turret recentred...");
        var scanned = false;
        for (var i = 0; i < world.Frames(100) && !scanned; i++)
        {
            world.Step();
            scanned = tank.TurretOf(0)!.Value.Angle != Fixed.Zero;
        }

        Check.That(scanned, "...and went back to glancing around.");
    }

    // ------------------------------------------------------------------------------------------------------------- real data

    /// <summary>Retail data: a Crusader that is sent after a Battlemaster keeps its turret on it while it drives, reloads and waits; retail objects with scan blocks scan within their scan range.</summary>
    public static void RealTurretAttackAndScan(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));

        var scanning = catalog.Templates.Where(template => template.Turrets is { } set && set.Turrets.Any(turret => turret is { CanScan: true })).Select(template => template.Name).ToArray();
        Console.WriteLine($"       scan: {scanning.Length} objects have a turret that glances around while idle (e.g. {string.Join(", ", scanning.Take(4))})");
        Check.That(scanning.Length >= 35 && scanning.Contains("AmericaVehicleSentryDrone") && scanning.Contains("GLAVehicleTechnicalChassisOne") && scanning.Contains("AmericaPatriotBattery"), "The retail scan blocks are read.");
        Check.That(!Template(catalog, "AmericaTankCrusader").Turrets![0]!.CanScan && !Template(catalog, "ChinaTankBattleMaster").Turrets![0]!.CanScan, "The Crusader and the Battlemaster do not scan.");
        var sentry = Template(catalog, "AmericaVehicleSentryDrone").Turrets![0]!;
        Check.That(sentry.MinScanIntervalTicks == TestRate.Frames(8) && sentry.MaxScanIntervalTicks == TestRate.Frames(8) && sentry.MaxScanAngle == Degrees(360m) && sentry.MinScanAngle == Fixed.Zero, "The Sentry Drone's block: a scan every 250 ms (8 ticks) up to 360 degrees off.");

        (ulong Hash, int Scans, Fixed Widest, bool Moved) Idle(string template, int frames)
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 9, players, "real-scan", fingerprint), catalog);
            var feeder = new Feeder(world);
            feeder.Spawn(1, template, owner: 1, x: 500, y: 500);
            world.Step();
            var unit = world.Entities.First(value => value.Template.Name == template);
            var scans = 0;
            var widest = Fixed.Zero;
            var previous = unit.TurretOf(0)!.Value.Angle;
            ulong hash = 0;
            for (var i = 0; i < world.Frames(frames); i++)
            {
                hash = world.Step().StateHash;
                var angle = unit.TurretOf(0)!.Value.Angle;
                if (previous == Fixed.Zero && angle != Fixed.Zero) scans++;
                if (angle.Abs() > widest) widest = angle.Abs();
                previous = angle;
            }

            return (hash, scans, widest, scans > 0);
        }

        foreach (var (name, maximum, minimumPeak) in new[] { ("GLAVehicleTechnicalChassisOne", 60m, 30m), ("AmericaVehicleSentryDrone", 360m, 5m), ("AmericaPatriotBattery", 360m, 5m) })
        {
            var run = Idle(name, 1800);
            Console.WriteLine($"       scan: {name} glanced around {run.Scans} times in 1800 ticks, widest {DegreesOf(run.Widest):F0} degrees");
            Check.That(run.Moved && run.Scans >= 3, $"{name} scans while idle ({run.Scans} times).");
            Check.That(DegreesOf(run.Widest) <= maximum + 0.5m && DegreesOf(run.Widest) >= minimumPeak, $"{name} stays within its scan range of {maximum} degrees.");
            Check.That(Idle(name, 1800).Hash == run.Hash, $"{name}: two runs agree.");
        }

        // A Crusader sent after a Battlemaster: it drives, the turret stays on the victim, the first shot needs no turning, and the aim holds across gaps in the orders.
        (ulong Hash, decimal Worst, int Shots, long Waits, bool StillAttacking) Chase()
        {
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "chase");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, players, "real-chase", fingerprint, terrain.Fingerprint), catalog, terrain);
            world.RevealMapPermanently(new PlayerId(1), true);
            world.RevealMapPermanently(new PlayerId(2), true);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "AmericaTankCrusader", owner: 1, x: 400, y: 400);
            feeder.Spawn(2, "ChinaTankBattleMaster", owner: 2, x: 700, y: 600, angle: Fixed.FromDecimal(1.2m));
            world.Step();
            var crusader = world.Entities.First(value => value.Template.Name == "AmericaTankCrusader");
            var victim = world.Entities.First(value => value.Template.Name == "ChinaTankBattleMaster");
            feeder.Pursue(1, crusader.Id.Value, victim.Id.Value, 700, 480);
            var worst = 0m;
            var shots = 0;
            var stopped = false;
            for (var tick = 0; tick < world.Frames(1200); tick++)
            {
                // The presentation's attack loop: chase until in reach, stop there, fire whenever the weapon is ready.
                var distance = DistanceBetween(crusader, victim);
                if (!stopped && distance <= world.MaxWeaponRange(crusader).ToDecimal() - 5m)
                {
                    stopped = true;
                    feeder.Pursue(1, crusader.Id.Value, victim.Id.Value, (int)crusader.X.Floor(), (int)crusader.Y.Floor());
                }

                if (stopped && tick % 2 == 0 && world.IsAnyWeaponReady(crusader) && !crusader.HasMoveTarget) feeder.FireWeapon(1, crusader.Id.Value, victim.Id.Value);
                shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == crusader.Id);
                if (tick >= world.Frames(60) && !victim.EffectivelyDead) worst = Math.Max(worst, AimError(crusader, victim));
                if (victim.EffectivelyDead) break;
            }

            return (world.StateHash, worst, shots, world.TurretWaits, world.AttackTargetOf(crusader) == victim.Id);
        }

        var chase = Chase();
        Console.WriteLine($"       chase: Crusader against Battlemaster - worst aim error from tick 60 {chase.Worst:F2} degrees, {chase.Shots} shots, {chase.Waits} orders waited");
        Check.That(chase.Worst <= 2.6m && chase.Shots >= 2, $"The Crusader kept its turret on the Battlemaster while it drove, stopped, reloaded and fired ({chase.Worst:F2} degrees worst, {chase.Shots} shots).");
        Check.That(chase.Hash == Chase().Hash, "The retail chase is deterministic.");
    }
}

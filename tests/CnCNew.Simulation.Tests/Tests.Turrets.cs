using System.Collections.Immutable;
using System.Text;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The turret model (docs/SIM-TURRET-AIM.md, <c>TurretAI</c>): the turret of a weapon slot turns at its own rate toward the victim of the fire orders, the weapon waits until it points
/// there, the turret holds, recentres and rests. Numbers are exact: 180 degrees per second are 6 degrees per tick, 500 ms are 15 ticks, the shot delay of 300 ms is 9 ticks.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> TurretFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/TurretFixture.ini"] = """
        Weapon TurretGun
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon QuickGun
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 100
        End

        Object TurretTank
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
            End
          End
        End
        Object PlainTank
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
          End
        End
        Object FakeTurretTank
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
              TurretTurnRate = 0
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object SwitchedOffTank
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
              InitiallyDisabled = Yes
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object SplitTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TurretGun
            Weapon = SECONDARY QuickGun
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 180
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object BareTurretTank
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
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object DualTurretTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TurretGun
            Weapon = SECONDARY QuickGun
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 30
              ControlledWeaponSlots = PRIMARY
            End
            AltTurret
              TurretTurnRate = 360
              TurretPitchRate = 60
              AllowsPitch = Yes
              FirePitch = 20
              ControlledWeaponSlots = SECONDARY
            End
            TurretsLinked = No
          End
        End
        Object LinkedTurretShip
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TurretGun
            Weapon = SECONDARY QuickGun
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 180
              ControlledWeaponSlots = PRIMARY
            End
            AltTurret
              TurretTurnRate = 60
              ControlledWeaponSlots = SECONDARY
            End
            TurretsLinked = Yes
          End
        End
        Object SweepTank
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
              TurretFireAngleSweep = PRIMARY 25
              TurretSweepSpeedModifier = PRIMARY 0.5
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object TurretTarget
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        Object FragileTarget
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) TurretWorld(params (string Template, int Owner, int X, int Y, decimal Heading)[] objects)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: TurretFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        foreach (var (template, owner, x, y, heading) in objects) feeder.Spawn(owner, template, owner: owner, x: x, y: y, angle: Fixed.FromDecimal(heading));
        world.Step();
        return (world, feeder, fixture);
    }

    /// <summary>Orders the shooter to fire every tick, as a standing attack does, until a shot goes out; returns the number of ticks that passed before the shot (-1 when none within <paramref name="limit"/> original frames).</summary>
    private static int TicksUntilShot(SimulationWorld world, Feeder feeder, uint shooter, uint victim, int limit = 80, int slot = WeaponSlot.Best, FireOrigin origin = FireOrigin.Player)
    {
        for (var waited = 0; waited < world.Frames(limit); waited++)
        {
            feeder.FireWeaponSlot(1, shooter, victim, slot, origin);
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity.Value == shooter)) return waited;
        }

        return -1;
    }

    private static Fixed Degrees(decimal degrees) => SimulationTiming.DegreesToRadians(degrees);

    private static bool Near(Fixed value, Fixed expected, decimal toleranceDegrees = 0.1m) => (value - expected).Abs() <= Degrees(toleranceDegrees);

    public static void TurretsTemplateModelAndStrictParsing()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: TurretFiles());
        var catalog = fixture.Templates;
        catalog.TryGet("TurretTank", out var tank);
        var set = tank!.Turrets!;
        var main = set[0]!;
        Check.That(set[1] is null && !set.Linked, "A Turret block gives the template its main turret.");
        Check.That(main.TurnRate == TestRate.Default.Time.DegreesPerSecondToRadiansPerTick(180m) && (main.TurnRate - FixedTrig.PiFraction(1, 30)).Abs() <= Fixed.Epsilon, $"TurretTurnRate is degrees per second: 180 are 6 degrees per tick ({main.TurnRate.Raw} raw).");
        Check.That(main.RecenterTicks == TestRate.Frames(15) && main.Controls(WeaponSlot.Primary) && !main.Controls(WeaponSlot.Secondary) && main.Gates && !main.AllowsPitch, "RecenterTime is 500 ms (15 ticks); ControlledWeaponSlots names the slot.");
        catalog.TryGet("BareTurretTank", out var bare);
        var bareTurret = bare!.Turrets![0]!;
        Check.That(bareTurret.TurnRate == TestRate.Time.PerOriginalFrameToPerTick(0.01m) && bareTurret.PitchRate == TestRate.Time.PerOriginalFrameToPerTick(0.01m) && bareTurret.RecenterTicks == TestRate.Frames(60) && bareTurret.NaturalAngle == Fixed.Zero,
            "Absent keys keep the reference defaults: 0.01 rad per frame, two seconds, natural angle 0.");
        catalog.TryGet("DualTurretTank", out var dual);
        Check.That(dual!.Turrets![0]!.Index == 0 && dual.Turrets[1] is { Index: 1, AllowsPitch: true } alt && alt.Controls(WeaponSlot.Secondary) && alt.FirePitch == Degrees(20m)
                   && alt.TurnRate == TestRate.Default.Time.DegreesPerSecondToRadiansPerTick(360m), "AltTurret is the second turret with its own rates, pitch and slot.");
        catalog.TryGet("LinkedTurretShip", out var ship);
        Check.That(ship!.Turrets!.Linked, "TurretsLinked of the AI module is read.");
        catalog.TryGet("SweepTank", out var sweep);
        var sweeper = sweep!.Turrets![0]!;
        Check.That(sweeper.SweepOf(WeaponSlot.Primary) == Degrees(25m) && sweeper.SweepOf(WeaponSlot.Secondary) == Fixed.Zero && sweeper.SweepSpeedOf(WeaponSlot.Primary) == Fixed.FromDecimal(0.5m)
                   && sweeper.SweepSpeedOf(WeaponSlot.Tertiary) == Fixed.One, "The sweep angle and speed are per weapon slot; the speed factor defaults to 1.");
        catalog.TryGet("FakeTurretTank", out var fake);
        catalog.TryGet("SwitchedOffTank", out var off);
        catalog.TryGet("PlainTank", out var plain);
        Check.That(!fake!.Turrets![0]!.Gates && !off!.Turrets![0]!.Gates && plain!.Turrets is null, "A turret that cannot turn or starts disabled does not aim; a tank without a block has no turret.");

        Dictionary<string, string> Files(string turretBlockBody) => new(SyntheticFixture.Files)
        {
            ["Data/INI/Object/TurretBad.ini"] = $"Object BadTurret\n  KindOf = SELECTABLE VEHICLE\n  Body = ActiveBody Body01\n    MaxHealth = 10.0\n    InitialHealth = 10.0\n  End\n  Behavior = AIUpdateInterface AI01\n    Turret\n{turretBlockBody}    End\n  End\nEnd\n",
        };

        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: Files("      ControlledWeaponSlots = PRIMARY\n      TurretWobble = 3\n")).Templates, "TurretWobble");
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: Files("      TurretTurnRate = 90\n")).Templates, "ControlledWeaponSlots");
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: Files("      ControlledWeaponSlots = QUARTERNARY\n")).Templates, "QUARTERNARY");
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: Files("      ControlledWeaponSlots = PRIMARY\n      TurretTurnRate = fast\n")).Templates, "TurretTurnRate");
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: Files("      ControlledWeaponSlots = PRIMARY\n      TurretFireAngleSweep = PRIMARY wide\n")).Templates, "TurretFireAngleSweep");
    }

    public static void TurretsTurnAtTheirRateAndTheShotWaits()
    {
        // Due +y of a tank facing +x: the turret has to turn 90 degrees at 6 degrees per tick.
        var (world, feeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var tank = Slotted(world, "TurretTank");
        var target = IdOf(world, "TurretTarget");
        Check.That(tank.TurretOf(0) is { Angle.Raw: 0, Rotating: false } && tank.TurretOf(1) is null, "A turret at rest sits at its natural angle and does not rotate.");

        feeder.FireWeapon(1, tank.Id.Value, target);
        world.Step();
        var first = tank.TurretOf(0)!.Value;
        // Above 30 Hz the first tick takes an equal share of the 30 Hz step: the rate per tick up to the rounding of the share (exact at 30 Hz).
        Check.That((first.Angle - TestRate.Default.Time.DegreesPerSecondToRadiansPerTick(180m)).Abs() <= (TestRate.IsOriginal ? Fixed.Zero : Fixed.Epsilon) && first.Rotating,$"The first tick turns the turret by its rate ({first.Angle.Raw} raw), TURRET_ROTATE is on.");
        var shotAfter = 1 + TicksUntilShot(world, feeder, tank.Id.Value, target);
        Check.That(shotAfter == world.Frames(15), $"The shot waits until the turret has turned 90 degrees at 6 degrees per tick: {shotAfter} ticks after the order.");
        var pointing = tank.TurretOf(0)!.Value;
        Check.That(Near(pointing.Angle, Degrees(90m), 1m) && !pointing.Rotating && world.TurretWaits == 15, $"It points at the victim when the shot goes out ({pointing.Angle.Raw} raw; {world.TurretWaits} orders waited).");
        Check.That(world.RejectionCounts.Sum() == 0, "Waiting for the turret is not a rejection (no 'cannot do' cue): nothing was rejected.");

        // A tank whose body already faces the victim does not wait at all: the turret angle is relative to the heading.
        var (turned, turnedFeeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 1.5708m), ("TurretTarget", 2, 0, 100, 0m));
        Check.That(TicksUntilShot(turned, turnedFeeder, IdOf(turned, "TurretTank"), IdOf(turned, "TurretTarget")) == 0, "Body heading counts: a tank already facing the victim fires on the first order.");
        Check.That(Near(Slotted(turned, "TurretTank").TurretOf(0)!.Value.Angle, Fixed.Zero), "...and its turret stays centred.");
    }

    public static void TurretsSomeWeaponsNeverWait()
    {
        // No turret, a turret that cannot turn (the Comanche's), one that starts disabled, a slot no turret controls, and a script's one-off command button: the shot goes out at once.
        foreach (var name in new[] { "PlainTank", "FakeTurretTank", "SwitchedOffTank" })
        {
            var (world, feeder, _) = TurretWorld((name, 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
            Check.That(TicksUntilShot(world, feeder, IdOf(world, name), IdOf(world, "TurretTarget")) == 0, $"{name} fires on its first order.");
            Check.That(world.TurretWaits == 0 && Near(Slotted(world, name).TurretOf(0)?.Angle ?? Fixed.Zero, Fixed.Zero), $"{name}: no turret movement, nothing waited.");
        }

        var (split, splitFeeder, _) = TurretWorld(("SplitTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var splitTank = Slotted(split, "SplitTank");
        Check.That(TicksUntilShot(split, splitFeeder, splitTank.Id.Value, IdOf(split, "TurretTarget"), slot: WeaponSlot.Secondary) == 0, "A weapon in a slot no turret controls fires at once.");
        Check.That(Near(splitTank.TurretOf(0)!.Value.Angle, Fixed.Zero), "...and the turret of the other slot is not asked to aim.");
        Check.That(TicksUntilShot(split, splitFeeder, splitTank.Id.Value, IdOf(split, "TurretTarget"), slot: WeaponSlot.Primary) > split.Frames(10), "The turret-controlled primary gun of the same tank waits for its turret.");

        var (script, scriptFeeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var scripted = Slotted(script, "TurretTank");
        Check.That(TicksUntilShot(script, scriptFeeder, scripted.Id.Value, IdOf(script, "TurretTarget"), origin: FireOrigin.Script) == 0, "A script's one-off fire order does not wait (it is never repeated)...");
        script.Step();
        Check.That(scripted.TurretOf(0)!.Value.Angle.Raw != 0, "...but the turret turns toward the victim all the same.");
    }

    public static void TurretsHoldRecentreAndRest()
    {
        var (world, feeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var tank = Slotted(world, "TurretTank");
        var target = IdOf(world, "TurretTarget");
        Check.That(TicksUntilShot(world, feeder, tank.Id.Value, target) == world.Frames(15), "The turret turned toward the target and fired.");
        Check.That(Encoding.UTF8.GetString(WorldSnapshot.Write(world)).Contains("\"turrets\""), "A turret away from rest is part of the snapshot.");

        // The fire order started an attack: with the victim alive the turret keeps its aim without further orders (docs/SIM-TURRET-AIM.md §3a).
        for (var i = 0; i < world.Frames(100); i++) world.Step();
        Check.That(Near(tank.TurretOf(0)!.Value.Angle, Degrees(90m), 1m) && !tank.TurretOf(0)!.Value.Rotating, "After the last order it still points at the living victim: the attack goes on.");

        // An isolated shot that is not an attack order (a script's command button) keeps its aim for the reload plus the recentre time, then turns back.
        var (shot, shotFeeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var shooter = Slotted(shot, "TurretTank");
        Check.That(TicksUntilShot(shot, shotFeeder, shooter.Id.Value, IdOf(shot, "TurretTarget"), origin: FireOrigin.Script) == 0, "A script's one-off shot goes out at once.");
        for (var i = 0; i < shot.Frames(22); i++) shot.Step();
        Check.That(Near(shooter.TurretOf(0)!.Value.Angle, Degrees(90m), 1m), "A one-off shot holds its aim for a while...");
        for (var i = 0; i < shot.Frames(200); i++) shot.Step();
        Check.That(shooter.TurretOf(0)!.Value.Angle == Fixed.Zero && !Encoding.UTF8.GetString(WorldSnapshot.Write(shot)).Contains("\"turrets\""),
            "...then it turns back at half speed and rests: a turret at rest writes nothing, the snapshot is the one of an object without turret state.");

        // A victim that dies: the turret keeps its angle for the hold time, then recentres (TurretAI::friend_getTurretTarget clears dead targets).
        var (killing, killFeeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("FragileTarget", 2, 0, 100, 0m));
        var killer = Slotted(killing, "TurretTank");
        var fragile = IdOf(killing, "FragileTarget");
        for (var i = 0; i < 4; i++)
        {
            killFeeder.FireWeapon(1, killer.Id.Value, fragile);
            killing.Step();
        }

        var halfway = killer.TurretOf(0)!.Value.Angle;
        killFeeder.Kill(fragile);
        for (var i = 0; i < killing.Frames(10); i++) killing.Step();
        Check.That(killer.TurretOf(0)!.Value.Angle == halfway && !killer.TurretOf(0)!.Value.Rotating, "A dead victim stops the turning: the turret holds where it was.");
        for (var i = 0; i < killing.Frames(120); i++) killing.Step();
        Check.That(killer.TurretOf(0)!.Value.Angle == Fixed.Zero, "...and recentres after the hold time.");
    }

    public static void TurretsTwoTurretsPitchAndSweep()
    {
        // The alternate turret serves the secondary slot, with its own rates and a fixed firing pitch; the main turret is not asked.
        var (world, feeder, _) = TurretWorld(("DualTurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var dual = Slotted(world, "DualTurretTank");
        var target = IdOf(world, "TurretTarget");
        var waited = TicksUntilShot(world, feeder, dual.Id.Value, target, slot: WeaponSlot.Secondary);
        var alt = dual.TurretOf(1)!.Value;
        Check.That(world.InFrames(waited) is >= 8 and <= 10, $"The secondary gun waits for the alt turret (90 degrees at 12 per tick) and its pitch (20 degrees at 2 per tick): {waited} ticks.");
        Check.That(Near(alt.Angle, Degrees(90m), 1m) && Near(alt.Pitch, Degrees(20m), 2.1m) && Near(dual.TurretOf(0)!.Value.Angle, Fixed.Zero), "The alt turret points at the victim at the firing pitch; the main turret stayed centred.");

        // A weapon that sweeps (TurretFireAngleSweep) may fire anywhere inside the sweep angle; the same turret without a sweep has to turn the last degrees first.
        var (sweepWorld, sweepFeeder, _) = TurretWorld(("SweepTank", 1, 0, 0, 0m), ("TurretTarget", 2, 94, 34, 0m));
        Check.That(TicksUntilShot(sweepWorld, sweepFeeder, IdOf(sweepWorld, "SweepTank"), IdOf(sweepWorld, "TurretTarget")) == 0, "A victim 20 degrees off, inside the 25 degree sweep, is fired at without turning first.");
        var (plainWorld, plainFeeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 94, 34, 0m));
        Check.That(plainWorld.InFrames(TicksUntilShot(plainWorld, plainFeeder, IdOf(plainWorld, "TurretTank"), IdOf(plainWorld, "TurretTarget"))) is >= 3 and <= 4, "Without the sweep the turret turns 20 degrees first.");

        // Linked turrets: every turret aims and any slot fires only once all of them point at the victim.
        var (ship, shipFeeder, _) = TurretWorld(("LinkedTurretShip", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var linked = Slotted(ship, "LinkedTurretShip");
        var shipWaited = TicksUntilShot(ship, shipFeeder, linked.Id.Value, IdOf(ship, "TurretTarget"), slot: WeaponSlot.Primary);
        Check.That(shipWaited >= ship.Frames(30) && Near(linked.TurretOf(1)!.Value.Angle, Degrees(90m), 2.1m), $"With TurretsLinked the fast main gun also waits for the slow alt turret (90 degrees at 2 per tick): {shipWaited} ticks.");
    }

    public static void TurretsSnapshotMidTurnAndDeterminism()
    {
        var (first, firstFeeder, fixture) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("DualTurretTank", 1, 40, 0, 0m), ("SweepTank", 1, 80, 0, 0m), ("TurretTarget", 2, 0, 100, 0m), ("TurretTarget", 2, 120, 100, 0m));
        var (second, secondFeeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("DualTurretTank", 1, 40, 0, 0m), ("SweepTank", 1, 80, 0, 0m), ("TurretTarget", 2, 0, 100, 0m), ("TurretTarget", 2, 120, 100, 0m));
        List<ulong> Run(SimulationWorld world, Feeder feeder, int frames)
        {
            var hashes = new List<ulong>();
            for (var i = 0; i < world.Frames(frames); i++)
            {
                feeder.FireWeapon(1, IdOf(world, "TurretTank"), IdOf(world, "TurretTarget"));
                feeder.FireWeaponSlot(1, IdOf(world, "DualTurretTank"), IdOf(world, "TurretTarget", 1), WeaponSlot.Secondary);
                if (world.Tick < world.Frames(60)) feeder.FireWeapon(1, IdOf(world, "SweepTank"), IdOf(world, "TurretTarget", 1));
                hashes.Add(world.Step().StateHash);
            }

            return hashes;
        }

        Check.That(Run(first, firstFeeder, 7).SequenceEqual(Run(second, secondFeeder, 7)), "Two independent runs agree while the turrets turn.");
        var bytes = WorldSnapshot.Write(first);
        Check.That(Encoding.UTF8.GetString(bytes).Contains("\"turrets\""), "The snapshot is taken mid-turn.");
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "The snapshot round-trips byte-identically.");
        var restoredFeeder = firstFeeder.ForkTo(restored);
        var a = Run(first, firstFeeder, 150);
        var b = Run(restored, restoredFeeder, 150);
        var c = Run(second, secondFeeder, 150);
        Check.That(a.SequenceEqual(b), "A world restored mid-turn continues identically.");
        Check.That(a.SequenceEqual(c), "The independent run produces the same hashes.");
        foreach (var name in new[] { "TurretTank", "DualTurretTank", "SweepTank" })
            for (var index = 0; index < 2; index++)
                Check.That(Slotted(first, name).TurretOf(index) == Slotted(restored, name).TurretOf(index), $"{name} turret {index} agrees after the restored run.");
    }

    // ------------------------------------------------------------------------------------------------------------- real data

    private static ObjectTemplate Template(TemplateCatalog catalog, string name) => catalog.TryGet(name, out var template) ? template : throw new InvalidOperationException(name);

    /// <summary>Retail data: every Turret/AltTurret block is typed and read; the Crusader, the Battlemaster, the Gattling Tank and the battleships carry the turrets the data gives them, and a Ranger has none.</summary>
    public static void RealTurrets(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);

        var withTurret = catalog.Templates.Where(template => template.Turrets is not null).ToArray();
        var alt = withTurret.Where(template => template.Turrets![1] is not null).ToArray();
        var fake = withTurret.Count(template => template.Turrets!.Turrets.Any(turret => turret is { Gates: false }));
        Console.WriteLine($"       turrets: {withTurret.Length} objects carry a turret ({withTurret.Count(template => template.IsInstantiable)} instantiable), {alt.Length} an alternate turret ({string.Join(", ", alt.Select(template => template.Name))}), {fake} a turret that does not gate fire");
        Check.That(withTurret.Length >= 200 && alt.Length == 4 && alt.Count(template => template.Turrets!.Linked) == 3 && !Template(catalog, "AmericaVehicleBattleShipReal").Turrets!.Linked,
            "The retail turret blocks are read; three of the four battleships link their two turrets (the cinematic one does not).");

        Check.That(catalog.TryGet("AmericaTankCrusader", out var crusader) && crusader.Turrets![0] is { } crusaderTurret && crusader.Turrets[1] is null
                   && crusaderTurret.TurnRate == TestRate.Default.Time.DegreesPerSecondToRadiansPerTick(180m) && crusaderTurret.Controls(WeaponSlot.Primary) && crusaderTurret.Gates, "The Crusader's turret turns at 180 degrees per second and controls its gun.");
        Check.That(catalog.TryGet("ChinaTankBattleMaster", out var battleMaster) && battleMaster.Turrets![0] is { Gates: true } battleMasterTurret && battleMasterTurret.TurnRate < TestRate.Default.Time.DegreesPerSecondToRadiansPerTick(180m),
            "The Battlemaster's turret is slower than the Crusader's.");
        Check.That(catalog.TryGet("AmericaInfantryRanger", out var ranger) && ranger.Turrets is null, "A Ranger has no turret: it fires without aiming.");
        var sweeping = withTurret.Where(template => template.Turrets!.Turrets.Any(turret => turret is not null && Enumerable.Range(0, WeaponSlot.Count).Any(slot => turret.SweepOf(slot) > Fixed.Zero))).ToArray();
        Check.That(sweeping.Length > 0, $"Gattling-type turrets sweep ({sweeping.Length} objects, e.g. {sweeping.FirstOrDefault()?.Name}).");

        var tanks = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        ulong Duel(out long crusaderShot, out long battleMasterShot, out Fixed crusaderError)
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, tanks, "real-turrets", fingerprint), catalog);
            var sequence = new Dictionary<int, ulong>();
            void Spawn(int player, string template, int x, int y, Fixed heading)
            {
                sequence[player] = sequence.GetValueOrDefault(player) + 1;
                Check.That(world.Submit(new CnCNew.Simulation.Commands.SpawnCommand(new PlayerId(player), sequence[player], world.Tick, template, new PlayerId(player), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, heading)).Accepted, $"{template} spawns.");
            }

            // The tank-duel of the demo: the Crusader faces +x with the Battlemaster 45 degrees off to its left, the Battlemaster faces the Crusader.
            Spawn(1, "AmericaTankCrusader", 1000, 1000, Fixed.Zero);
            Spawn(2, "ChinaTankBattleMaster", 1085, 1085, FixedTrig.Normalize(FixedTrig.Pi + FixedTrig.Pi / Fixed.FromInt(4)));
            world.Step();
            var crusaderEntity = world.Entities.First(value => value.Template.Name == "AmericaTankCrusader");
            var battleMasterEntity = world.Entities.First(value => value.Template.Name == "ChinaTankBattleMaster");
            crusaderShot = battleMasterShot = -1;
            var start = world.Tick;
            crusaderError = Fixed.Zero;
            for (var i = 0; i < world.Frames(90); i++)
            {
                foreach (var (shooter, victim) in new[] { (crusaderEntity, battleMasterEntity), (battleMasterEntity, crusaderEntity) })
                {
                    sequence[shooter.Owner.Value]++;
                    world.Submit(new CnCNew.Simulation.Commands.FireWeaponCommand(shooter.Owner, sequence[shooter.Owner.Value], world.Tick, shooter.Id, victim.Id));
                }

                var tick = world.Tick;
                var result = world.Step();
                foreach (var shot in result.Events.Where(value => value.Kind == SimEventKind.WeaponFired))
                {
                    if (shot.Entity == crusaderEntity.Id && crusaderShot < 0)
                    {
                        crusaderShot = tick - start;
                        var bearing = FixedTrig.Atan2(battleMasterEntity.Y - crusaderEntity.Y, battleMasterEntity.X - crusaderEntity.X);
                        crusaderError = FixedTrig.AngleDifference(crusaderEntity.Angle + crusaderEntity.TurretOf(0)!.Value.Angle, bearing).Abs();
                    }
                    else if (shot.Entity == battleMasterEntity.Id && battleMasterShot < 0) battleMasterShot = tick - start;
                }
            }

            return world.StateHash;
        }

        var first = Duel(out var crusaderShot, out var battleMasterShot, out var error);
        Console.WriteLine($"       turrets: tank duel - the Battlemaster fires after {battleMasterShot} ticks (already aimed), the Crusader after {crusaderShot} ticks (45 degrees at 6 per tick), aim error {error.Raw / (double)Fixed.ScaleRaw * 180 / Math.PI:F2} degrees");
        Check.That(battleMasterShot == 0, "The Battlemaster, aimed from the start, fires on its first order.");
        Check.That(TestRate.InFrames(crusaderShot) is >= 7 and <= 9 && error < Degrees(2m), $"The Crusader's turret turns 45 degrees at 6 degrees per tick before the shot ({crusaderShot} ticks, error {error.Raw} raw).");
        Check.That(first == Duel(out _, out _, out _), "The retail duel is deterministic.");
    }
}

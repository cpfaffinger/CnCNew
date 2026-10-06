using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The units' own target acquisition and the attack-move (docs/SIMULATION-CORE.md §33): an idle unit picks its victim itself, the turret of a marching unit takes targets of opportunity,
/// an attack-move stops for a fight and marches on, and an attack the command giver orders is left alone.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> AcquisitionFiles() => new Dictionary<string, string>(TurretAttackFiles())
    {
        ["Data/INI/Object/AcquisitionFixture.ini"] = """
        Weapon AcqGun
          PrimaryDamage = 10
          AttackRange = 100.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End

        Object AcqSoldier
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AcqGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
        End
        Object AcqMute
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AcqGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = No ATTACK_BUILDINGS
          End
        End
        Object AcqSneak
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AcqGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes Stealthed NotWhileAttacking
            MoodAttackCheckRate = 250
          End
        End
        Object AcqSiege
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AcqGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = YES ATTACK_BUILDINGS
            MoodAttackCheckRate = 250
          End
        End
        Object AcqTank
          KindOf = SELECTABLE VEHICLE
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TurretGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
            Turret
              TurretTurnRate = 360
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object AcqDummy
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 10000.0
            InitialHealth = 10000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object AcqWeak
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 30.0
            InitialHealth = 30.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AcqHut
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 10000.0
            InitialHealth = 10000.0
          End
        End
        Object AcqBarracks
          KindOf = SELECTABLE STRUCTURE FS_BARRACKS
          Body = ActiveBody Body01
            MaxHealth = 10000.0
            InitialHealth = 10000.0
          End
        End
        Object AcqBunker
          KindOf = SELECTABLE STRUCTURE FS_BASE_DEFENSE
          Body = ActiveBody Body01
            MaxHealth = 10000.0
            InitialHealth = 10000.0
          End
        End
        """,
    };

    private static readonly ImmutableArray<PlayerConfiguration> ComputerAgainstHuman = [new(new PlayerId(1), "Alpha", IsComputer: true), new(new PlayerId(2), "Beta")];

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) AcquisitionWorld(bool computer, params (string Template, int Owner, int X, int Y)[] objects)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: AcquisitionFiles());
        var world = computer
            ? new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, ComputerAgainstHuman, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates)
            : NewWorld(fixture);
        var feeder = new Feeder(world);
        foreach (var (template, owner, x, y) in objects) feeder.Spawn(owner, template, owner: owner, x: x, y: y);
        world.Step();
        return (world, feeder, fixture);
    }

    private static Fixed Hurt(Entity entity) => entity.Body!.MaxHealth - entity.Body.Health;

    private static int ShotsOf(TickResult result, Entity source) => result.Events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == source.Id);

    private static AIUpdateInterface AiOfUnit(Entity entity) => entity.Modules.OfType<AIUpdateInterface>().First();

    public static void AcquisitionFlagsAreTypedAndStrict()
    {
        var (world, _, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqMute", 1, 0, 500), ("AcqSneak", 1, 0, 1000), ("AcqSiege", 1, 0, 1500), ("AcqDummy", 1, 0, 2000));
        Check.That(AiOfUnit(Slotted(world, "AcqSoldier")).AutoAcquire == AutoAcquire.Idle, "Yes is the idle bit.");
        Check.That(AiOfUnit(Slotted(world, "AcqMute")).AutoAcquire == (AutoAcquire.No | AutoAcquire.AttackBuildings), "No ATTACK_BUILDINGS sets two bits, none of them the idle bit.");
        Check.That(AiOfUnit(Slotted(world, "AcqSneak")).AutoAcquire == (AutoAcquire.Idle | AutoAcquire.Stealthed | AutoAcquire.NotWhileAttacking), "Yes Stealthed NotWhileAttacking (names are case-insensitive).");
        Check.That(AiOfUnit(Slotted(world, "AcqSiege")).AutoAcquire == (AutoAcquire.Idle | AutoAcquire.AttackBuildings), "YES ATTACK_BUILDINGS.");
        Check.That(AiOfUnit(Slotted(world, "AcqDummy")).AutoAcquire == AutoAcquire.None, "Without the key no bit is set (the reference's constructor value 0).");
        Check.That(AiOfUnit(Slotted(world, "AcqSoldier")).MoodCheckTicks == world.Frames(8) && AiOfUnit(Slotted(world, "AcqDummy")).MoodCheckTicks == world.Frames(60), "MoodAttackCheckRate 250 ms is 8 ticks; the default is two seconds.");

        Dictionary<string, string> Files(string value) => new(SyntheticFixture.Files)
        {
            ["Data/INI/Object/AcqBad.ini"] = $"Object AcqBad\n  KindOf = SELECTABLE VEHICLE\n  Body = ActiveBody Body01\n    MaxHealth = 10.0\n    InitialHealth = 10.0\n  End\n  Behavior = AIUpdateInterface AI01\n    AutoAcquireEnemiesWhenIdle = {value}\n  End\nEnd\n",
        };
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: Files("Yes Sometimes")).Templates, "Sometimes");
        Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: Files("Yes +STEALTHED")).Templates, "mixed");
    }

    public static void AcquisitionIdleUnitPicksTheClosestEnemyInRange()
    {
        var (world, feeder, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqDummy", 2, 60, 0), ("AcqDummy", 2, 90, 0), ("AcqMute", 1, 0, 500), ("AcqDummy", 2, 60, 500), ("AcqSoldier", 1, 0, 1000), ("AcqDummy", 2, 140, 1000));
        var soldier = Slotted(world, "AcqSoldier");
        var near = Slotted(world, "AcqDummy");
        var far = Slotted(world, "AcqDummy", 1);
        var mutesVictim = Slotted(world, "AcqDummy", 2);
        var outOfReach = Slotted(world, "AcqDummy", 3);
        var firstShot = -1L;
        for (var step = 0; step < world.Frames(40); step++)
        {
            var result = world.Step();
            if (firstShot < 0 && ShotsOf(result, soldier) > 0) firstShot = result.Tick;
        }

        Check.That(world.InFrames(firstShot) is >= 2 and <= 4, $"The idle soldier looks two frames after it became idle and fires at once (first shot at tick {firstShot}).");
        Check.That(world.AcquiredVictimOf(soldier) == near.Id && Hurt(near) >= Fixed.FromInt(30) && Hurt(far) == Fixed.Zero, $"It took the closer of two enemies in range and keeps shooting it ({Hurt(near)} damage, {Hurt(far)} on the other).");
        Check.That(Hurt(mutesVictim) == Fixed.Zero && !world.AcquiredVictimOf(Slotted(world, "AcqMute")).IsValid, "A unit whose AI says No (even with ATTACK_BUILDINGS) never acquires.");
        Check.That(Hurt(outOfReach) == Fixed.Zero && !Slotted(world, "AcqSoldier", 1).HasMoveTarget, "An enemy beyond the weapon's reach is neither shot nor walked to by a human player's unit.");

        // The victim leaves: a human player's unit does not follow and turns to the next one in range.
        feeder.SetMoveTarget(2, near.Id.Value, 400, 0);
        for (var step = 0; step < world.Frames(60); step++) world.Step();
        Check.That(world.AcquiredVictimOf(soldier) == far.Id && Hurt(far) > Fixed.Zero, "When its victim walks out of range it takes the next enemy in range.");
        Check.That(soldier.X == Fixed.Zero && soldier.Y == Fixed.Zero && !soldier.HasMoveTarget, "It did not move.");

        // Nothing fires by itself when the host switches the acquisition off.
        var (quiet, _, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqDummy", 2, 60, 0));
        quiet.SelfAcquisition = false;
        var shots = 0;
        for (var step = 0; step < quiet.Frames(80); step++) shots += quiet.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired);
        Check.That(shots == 0 && Slotted(quiet, "AcqSoldier").Modules.Count > 0, "With SelfAcquisition off nobody picks a victim.");
    }

    public static void AcquisitionKillsOneVictimAfterAnother()
    {
        var (world, _, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqWeak", 2, 50, 0), ("AcqWeak", 2, 0, 80));
        var soldier = Slotted(world, "AcqSoldier");
        var deaths = new List<long>();
        var shots = new List<long>();
        for (var step = 0; step < world.Frames(120); step++)
        {
            var result = world.Step();
            if (ShotsOf(result, soldier) > 0) shots.Add(result.Tick);
            deaths.AddRange(result.Events.Where(value => value.Kind == SimEventKind.EntityDied).Select(_ => result.Tick));
        }

        Check.That(deaths.Count == 2 && shots.Count == 6, $"Three shots each kill both targets ({shots.Count} shots, deaths at {string.Join(", ", deaths)}).");
        // The weapon reloads 9 ticks; the next victim is taken two frames after the kill, so the fourth shot is not later than the reload allows.
        Check.That(shots[3] - shots[2] <= world.Frames(10), $"The second victim is shot as soon as the weapon is ready again (shots at {string.Join(", ", shots)}).");
        Check.That(!world.AcquiredVictimOf(soldier).IsValid, "With nobody left the soldier attacks nothing.");
    }

    public static void AcquisitionStructuresStealthAndMoods()
    {
        // A human player's unit leaves ordinary enemy buildings alone, shoots base defenses; ATTACK_BUILDINGS and a computer player take every enemy structure.
        var (human, _, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqHut", 2, 50, 0), ("AcqBarracks", 2, 0, 50), ("AcqBunker", 2, -70, 0), ("AcqSiege", 1, 0, 1000), ("AcqBarracks", 2, 50, 1000));
        for (var step = 0; step < human.Frames(60); step++) human.Step();
        Check.That(Hurt(Slotted(human, "AcqHut")) == Fixed.Zero && Hurt(Slotted(human, "AcqBarracks")) == Fixed.Zero && Hurt(Slotted(human, "AcqBunker")) > Fixed.Zero,
            "A human player's soldier ignores the hut and the barracks next to it and shoots the base defense further away.");
        Check.That(Hurt(Slotted(human, "AcqBarracks", 1)) > Fixed.Zero, "A unit with ATTACK_BUILDINGS shoots the barracks.");
        var (computer, _, _) = AcquisitionWorld(true, ("AcqSoldier", 1, 0, 0), ("AcqBarracks", 2, 50, 0));
        for (var step = 0; step < computer.Frames(60); step++) computer.Step();
        Check.That(Hurt(Slotted(computer, "AcqBarracks")) > Fixed.Zero, "A computer player's soldier shoots the enemy barracks.");

        // Stealth: an undetected stealthed enemy is not seen; a stealthed unit only fires by itself with the Stealthed bit.
        var stealthed = 1UL << ObjectStatusBits.Index("STEALTHED");
        var detected = 1UL << ObjectStatusBits.Index("DETECTED");
        var (hide, _, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqDummy", 2, 60, 0), ("AcqSoldier", 1, 0, 1000), ("AcqDummy", 2, 60, 1000), ("AcqSneak", 1, 0, 2000), ("AcqDummy", 2, 60, 2000));
        hide.SetStatus(Slotted(hide, "AcqDummy").Id, stealthed);
        hide.SetStatus(Slotted(hide, "AcqSoldier", 1).Id, stealthed);
        hide.SetStatus(Slotted(hide, "AcqSneak").Id, stealthed);
        for (var step = 0; step < hide.Frames(60); step++) hide.Step();
        Check.That(Hurt(Slotted(hide, "AcqDummy")) == Fixed.Zero, "A stealthed, undetected enemy is not acquired.");
        Check.That(Hurt(Slotted(hide, "AcqDummy", 1)) == Fixed.Zero, "A stealthed unit with a plain Yes does not give itself away.");
        Check.That(Hurt(Slotted(hide, "AcqDummy", 2)) > Fixed.Zero, "A stealthed unit with Yes Stealthed fires.");
        hide.SetStatus(Slotted(hide, "AcqDummy").Id, detected);
        for (var step = 0; step < hide.Frames(30); step++) hide.Step();
        Check.That(Hurt(Slotted(hide, "AcqDummy")) > Fixed.Zero, "Once detected the enemy is acquired.");

        // Moods and attack priorities come from the host (the script engine of a mission); only a computer player's units have a mood.
        var (moody, _, _) = AcquisitionWorld(true, ("AcqSoldier", 1, 0, 0), ("AcqDummy", 2, 40, 0), ("AcqWeak", 2, 0, 90));
        var advisor = new FixedAdvisor { Mood = Attitude.Sleep };
        moody.AcquisitionAdvisor = advisor;
        for (var step = 0; step < moody.Frames(60); step++) moody.Step();
        Check.That(Hurt(Slotted(moody, "AcqDummy")) == Fixed.Zero, "A sleeping team's unit does not acquire.");
        advisor.Mood = Attitude.Normal;
        advisor.Priorities = target => target.Template.Name == "AcqWeak" ? 5 : 0;
        for (var step = 0; step < moody.Frames(40); step++) moody.Step();
        Check.That(Hurt(Slotted(moody, "AcqDummy")) == Fixed.Zero && !moody.Entities.Any(value => value.Template.Name == "AcqWeak"),
            "With a priority set the unit shoots the farther target of priority 5 and never the nearer one of priority 0.");
        advisor.Priorities = target => target.Template.Name == "AcqWeak" ? 2 : 1;
        var (ranked, _, _) = AcquisitionWorld(true, ("AcqSoldier", 1, 0, 0), ("AcqDummy", 2, 40, 0), ("AcqWeak", 2, 0, 90));
        ranked.AcquisitionAdvisor = advisor;
        for (var step = 0; step < ranked.Frames(6); step++) ranked.Step();
        Check.That(ranked.AcquiredVictimOf(Slotted(ranked, "AcqSoldier")) == Slotted(ranked, "AcqWeak").Id, "Under 100 units the distance takes nothing off: priority 2 beats the closer priority 1.");
    }

    private sealed class FixedAdvisor : IAcquisitionAdvisor
    {
        public Attitude Mood { get; set; } = Attitude.Normal;
        public Func<Entity, int>? Priorities { get; set; }

        public bool TryAttackPriority(Entity attacker, Entity target, out int priority)
        {
            priority = Priorities?.Invoke(target) ?? 1;
            return Priorities is not null;
        }

        public Attitude AttitudeOf(Entity entity) => Mood;
    }

    public static void AcquisitionLeavesAnOrderedAttackAlone()
    {
        var (world, feeder, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqDummy", 2, 50, 0), ("AcqDummy", 2, 90, 0));
        var soldier = Slotted(world, "AcqSoldier");
        var near = Slotted(world, "AcqDummy");
        var far = Slotted(world, "AcqDummy", 1);
        var shots = 0;
        for (var step = 0; step < world.Frames(90); step++)
        {
            feeder.FireWeapon(1, soldier.Id.Value, far.Id.Value);
            shots += ShotsOf(world.Step(), soldier);
        }

        Check.That(Hurt(near) == Fixed.Zero && Hurt(far) == Fixed.FromInt(10 * shots) && shots is >= 9 and <= 10, $"While its player orders the attack the unit shoots only the ordered victim, once per reload ({shots} shots in 90 ticks).");
        Check.That(world.AcquiredVictimOf(soldier) == far.Id, "The ordered victim is the unit's victim.");
        for (var step = 0; step < world.Frames(30); step++) world.Step();
        Check.That(Hurt(near) == Fixed.Zero, "A gap of 30 ticks in the orders does not end the ordered attack.");
        for (var step = 0; step < world.Frames(60); step++) world.Step();
        Check.That(Hurt(near) > Fixed.Zero && world.AcquiredVictimOf(soldier) == near.Id, "Once the orders have stopped for good the unit picks its victims itself again (the closest).");

        // A move order ends the unit's own attack; on the march a unit without a turret does not shoot, a turret takes targets of opportunity.
        feeder.SetMoveTarget(1, soldier.Id.Value, 0, -30);
        world.Step();
        Check.That(!world.AcquiredVictimOf(soldier).IsValid && soldier.HasMoveTarget, "A move order ends the attack.");

        var (march, marchFeeder, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqDummy", 2, 300, 60), ("AcqTank", 1, 0, 1000), ("AcqDummy", 2, 300, 1100));
        var walker = Slotted(march, "AcqSoldier");
        var tank = Slotted(march, "AcqTank");
        marchFeeder.SetMoveTarget(1, walker.Id.Value, 600, 0);
        marchFeeder.SetMoveTarget(1, tank.Id.Value, 600, 1000);
        var tankShotsMoving = 0;
        var tankStopped = false;
        var lastX = tank.X;
        for (var step = 0; step < march.Frames(215); step++)
        {
            var result = march.Step();
            if (tank.HasMoveTarget) tankShotsMoving += ShotsOf(result, tank);
            if (step > 1 && tank.HasMoveTarget && tank.X <= lastX) tankStopped = true;
            lastX = tank.X;
        }

        Check.That(Hurt(Slotted(march, "AcqDummy")) == Fixed.Zero && walker.X > Fixed.FromInt(590), "A marching unit without a turret passes an enemy in range without shooting.");
        Check.That(tankShotsMoving >= 3 && !tankStopped && tank.X > Fixed.FromInt(590), $"The turret of a marching tank shoots what comes into range ({tankShotsMoving} shots on the move) and the tank drives on.");
    }

    public static void AttackMoveStopsFightsAndMarchesOn()
    {
        // A human player's unit: it stops when an enemy is in weapon range, kills it and marches on; it does not leave its way for an enemy out of reach.
        var (world, feeder, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqWeak", 2, 300, 40), ("AcqWeak", 2, 450, 170));
        var soldier = Slotted(world, "AcqSoldier");
        var inReach = Slotted(world, "AcqWeak");
        var offTheWay = Slotted(world, "AcqWeak", 1);
        Check.That(feeder.AttackMove(1, soldier.Id.Value, 600, 0).Accepted, "The attack-move order is accepted.");
        world.Step();
        Check.That(world.IsAttackMoving(soldier) && world.AttackMoveGoalOf(soldier) == (Fixed.FromInt(600), Fixed.FromInt(0)) && soldier.HasMoveTarget, "The unit is on an attack-move to its goal.");
        var halted = Fixed.Zero;
        var haltTicks = 0;
        var shots = 0;
        var arrived = -1;
        for (var step = 0; step < world.Frames(400) && arrived < 0; step++)
        {
            var result = world.Step();
            shots += ShotsOf(result, soldier);
            if (world.IsAttackMoving(soldier) && !soldier.HasMoveTarget)
            {
                haltTicks++;
                halted = soldier.X;
            }

            if (!world.IsAttackMoving(soldier)) arrived = step;
        }

        Check.That(haltTicks >= world.Frames(18) && halted > Fixed.FromInt(200) && halted < Fixed.FromInt(400), $"It stopped on the way for the fight ({haltTicks} ticks at x = {halted}).");
        Check.That(inReach.EffectivelyDead && shots == 3, $"It killed the enemy in reach with three shots ({shots}).");
        Check.That(arrived > 0 && soldier.X > Fixed.FromInt(598) && soldier.Y.Abs() < Fixed.FromInt(2) && !soldier.HasMoveTarget, $"Then it marched on to its goal ({soldier.X}, {soldier.Y}).");
        Check.That(Hurt(offTheWay) == Fixed.Zero && !offTheWay.EffectivelyDead, "The enemy beyond its weapon's reach was left alone: a human player's unit does not chase.");

        // The same order as a plain move: nothing is shot.
        var (plain, plainFeeder, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqWeak", 2, 300, 40));
        plainFeeder.SetMoveTarget(1, Slotted(plain, "AcqSoldier").Id.Value, 600, 0);
        for (var step = 0; step < plain.Frames(220); step++) plain.Step();
        Check.That(Hurt(Slotted(plain, "AcqWeak")) == Fixed.Zero && Slotted(plain, "AcqSoldier").X > Fixed.FromInt(598), "A plain move walks past the same enemy.");

        // A computer player's unit walks to an enemy it sees beyond its weapons (vision 150 x 1.333 = 200), kills it and returns to its march.
        var (hunt, huntFeeder, _) = AcquisitionWorld(true, ("AcqSoldier", 1, 0, 0), ("AcqWeak", 2, 300, 170));
        var hunter = Slotted(hunt, "AcqSoldier");
        var prey = Slotted(hunt, "AcqWeak");
        huntFeeder.AttackMove(1, hunter.Id.Value, 600, 0);
        var widest = Fixed.Zero;
        var done = -1;
        for (var step = 0; step < hunt.Frames(500) && done < 0; step++)
        {
            hunt.Step();
            widest = Fixed.Max(widest, hunter.Y);
            if (step > 2 && !hunt.IsAttackMoving(hunter)) done = step;
        }

        Check.That(prey.EffectivelyDead && widest > Fixed.FromInt(50), $"The computer player's unit left its way ({widest} off) and killed the enemy.");
        Check.That(done > 0 && hunter.X > Fixed.FromInt(598) && hunter.Y.Abs() < Fixed.FromInt(2), $"Then it resumed the march and arrived ({hunter.X}, {hunter.Y}).");

        // A new order ends the attack-move.
        var (stop, stopFeeder, _) = AcquisitionWorld(false, ("AcqSoldier", 1, 0, 0), ("AcqDummy", 2, 300, 40));
        var stopper = Slotted(stop, "AcqSoldier");
        stopFeeder.AttackMove(1, stopper.Id.Value, 600, 0);
        for (var step = 0; step < stop.Frames(120); step++) stop.Step();
        Check.That(stop.IsAttackMoving(stopper) && !stopper.HasMoveTarget && stop.AcquiredVictimOf(stopper).IsValid, "It stands and fights an enemy it cannot kill quickly.");
        stopFeeder.SetMoveTarget(1, stopper.Id.Value, 0, 0);
        stop.Step();
        Check.That(!stop.IsAttackMoving(stopper) && !stop.AcquiredVictimOf(stopper).IsValid && stopper.HasMoveTarget, "A move order ends the attack-move and the fight.");
    }

    public static void AttackMoveSnapshotDeterminismAndReplay()
    {
        (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) Start()
        {
            var setup = AcquisitionWorld(true, ("AcqSoldier", 1, 0, 0), ("AcqDummy", 2, 300, 170), ("AcqTank", 1, 0, 400), ("AcqWeak", 2, 250, 430), ("AcqSoldier", 2, 900, 0));
            setup.Feeder.AttackMove(1, Slotted(setup.World, "AcqSoldier").Id.Value, 600, 0);
            setup.Feeder.AttackMove(1, Slotted(setup.World, "AcqTank").Id.Value, 600, 400);
            return setup;
        }

        var (first, _, fixture) = Start();
        var (second, _, _) = Start();
        for (var step = 0; step < first.Frames(90); step++) Check.That(first.Step().StateHash == second.Step().StateHash, $"Two worlds agree (step {step}).");
        var soldier = Slotted(first, "AcqSoldier");
        Check.That(first.IsAttackMoving(soldier) && first.AcquiredVictimOf(soldier).IsValid, "The snapshot is taken in the middle of a fight on the attack-move.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(first), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == first.StateHash, "The attack state restores to the same hash.");
        Check.That(restored.IsAttackMoving(restored.FindEntity(soldier.Id)!) && restored.AcquiredVictimOf(restored.FindEntity(soldier.Id)!) == first.AcquiredVictimOf(soldier), "Victim and goal survive the snapshot.");
        for (var step = 0; step < restored.Frames(400); step++) Check.That(restored.Step().StateHash == first.Step().StateHash && second.Step().StateHash == first.StateHash, $"The restored world follows the original (step {step}).");

        var player1 = new PlayerId(1);
        var player2 = new PlayerId(2);
        var commands = new List<SimCommand>
        {
            new SpawnCommand(player1, 1, 0, "AcqSoldier", player1, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero),
            new SpawnCommand(player2, 1, 0, "AcqWeak", player2, Fixed.FromInt(300), Fixed.FromInt(40), Fixed.Zero, Fixed.Zero),
            new AttackMoveCommand(player1, 2, 3, new EntityId(1), Fixed.FromInt(600), Fixed.Zero),
        };
        var ordered = commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence).ToImmutableArray();
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, fixture.Fingerprint, SyntheticFixture.FixtureId, 1, new WorldId(1), TwoPlayers, 300, 20);
        var probe = ReplayRunner.Run(new ReplayFile(header, ordered, []), fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        var file = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, ordered, probe.Checkpoints)));
        Check.That(file.Commands.SequenceEqual(ordered) && ordered.OfType<AttackMoveCommand>().Single().TypeCode == CommandTypes.AttackMove && CommandTypes.AttackMove == 32, "AttackMoveCommand (type 32) survives the replay file.");
        var again = ReplayRunner.Run(file, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(again.TickHashes.AsSpan().SequenceEqual(probe.TickHashes) && again.VerifiedCheckpoints == file.Checkpoints.Length, "The replay reproduces the march and the fight tick by tick.");
    }

    public static void RealAcquisition(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var ignored = graph.Diagnostics.Count(value => value.Code == "compatibility-ignored-tokens" && value.Message.Contains("AutoAcquireEnemiesWhenIdle", StringComparison.Ordinal));
        Check.That(ignored == 0, $"No AutoAcquireEnemiesWhenIdle value loses tokens any more ({ignored}).");

        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        AutoAcquire FlagsOf(string template)
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, "real-acquire-flags", fingerprint), catalog);
            new Feeder(world).Spawn(1, template, owner: 1, x: 100, y: 100);
            world.Step();
            return AiOfUnit(world.Entities.First(value => value.Template.Name == template)).AutoAcquire;
        }

        Check.That(FlagsOf("AmericaTankCrusader") == AutoAcquire.Idle, "The Crusader: Yes.");
        Check.That(FlagsOf("AmericaInfantryPathfinder") == (AutoAcquire.Idle | AutoAcquire.Stealthed), "The Pathfinder: Yes Stealthed (it fires from stealth).");
        var burton = FlagsOf("AmericaInfantryColonelBurton");
        var pathfinder = FlagsOf("AmericaInfantryPathfinder");
        Console.WriteLine($"       acquire: Colonel Burton {burton}, Pathfinder {pathfinder}, Patriot {FlagsOf("AmericaPatriotBattery")}, Dozer {FlagsOf("AmericaVehicleDozer")}, Jarmen Kell {FlagsOf("GLAInfantryJarmenKell")}");

        // Two enemy tanks in range of each other and nobody giving orders: they fight it out by themselves, deterministically.
        (ulong Hash, int Shots, bool OneDied, long FirstShot) Duel()
        {
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "acquire");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, players, "real-acquire", fingerprint, terrain.Fingerprint), catalog, terrain);
            world.RevealMapPermanently(new PlayerId(1), true);
            world.RevealMapPermanently(new PlayerId(2), true);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "AmericaTankCrusader", owner: 1, x: 400, y: 400);
            feeder.Spawn(2, "ChinaTankBattleMaster", owner: 2, x: 500, y: 400, angle: Fixed.FromDecimal(1.2m));
            world.Step();
            var shots = 0;
            var first = -1L;
            for (var tick = 0; tick < world.Frames(2400); tick++)
            {
                var result = world.Step();
                var fired = result.Events.Count(value => value.Kind == SimEventKind.WeaponFired);
                if (fired > 0 && first < 0) first = result.Tick;
                shots += fired;
                if (world.Entities.Count(value => value.Template.IsKindOf("VEHICLE") && !value.EffectivelyDead) < 2) break;
            }

            return (world.StateHash, shots, world.Entities.Count(value => value.Template.IsKindOf("VEHICLE") && !value.EffectivelyDead) < 2, first);
        }

        var duel = Duel();
        Console.WriteLine($"       acquire: Crusader against Battlemaster without orders - first shot at tick {duel.FirstShot}, {duel.Shots} shots, one destroyed: {duel.OneDied}");
        Check.That(duel.Shots > 4 && duel.OneDied && duel.FirstShot is > 0 and < 120, "Two retail tanks in range of each other fight without any order until one is destroyed.");
        Check.That(Duel().Hash == duel.Hash, "The duel is deterministic.");
    }
}

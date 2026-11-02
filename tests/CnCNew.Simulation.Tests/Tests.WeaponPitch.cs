using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// <c>Weapon::isWithinTargetPitch</c> and <c>DAMAGE_STATUS</c>: a weapon looks at the pitch of its victim, and the Avenger's designator marks a target. Before, neither key was read, so the designator
/// did nothing at all: its hit came to no damage and no status.
/// </summary>
static partial class Tests
{
    private const string PitchIni = """
        Weapon FlatGun
          PrimaryDamage = 10
          PrimaryDamageRadius = 1.0
          AttackRange = 500.0
          DelayBetweenShots = 100
          AntiAirborneVehicle = Yes
          AntiGround = Yes
          MinTargetPitch = -15
          MaxTargetPitch = 15
        End
        Weapon SkyGun
          PrimaryDamage = 10
          PrimaryDamageRadius = 1.0
          AttackRange = 500.0
          DelayBetweenShots = 100
          AntiAirborneVehicle = Yes
          AntiGround = Yes
          MinTargetPitch = 45
          MaxTargetPitch = 80
        End
        Weapon MarkGun
          PrimaryDamage = 200
          PrimaryDamageRadius = 1.0
          AttackRange = 500.0
          DelayBetweenShots = 100
          DamageType = STATUS
          DamageStatusType = FAERIE_FIRE
        End
        Object FlatShooter
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FlatGun
          End
        End
        Object SkyShooter
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SkyGun
          End
        End
        Object Designator
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MarkGun
          End
        End
        Object PitchTarget
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) PitchWorld(SimulationRate rate)
    {
        var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Pitch.ini"] = PitchIni });
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world), fixture);
    }

    public static void AWeaponLooksAtThePitchOfItsVictim()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, _) = PitchWorld(rate);
            feeder.Spawn(1, "FlatShooter", owner: 1, x: 100, y: 100);
            feeder.Spawn(1, "SkyShooter", owner: 1, x: 100, y: 300);
            feeder.Spawn(2, "PitchTarget", owner: 2, x: 200, y: 100);
            feeder.Spawn(2, "PitchTarget", owner: 2, x: 120, y: 300);
            feeder.Spawn(2, "PitchTarget", owner: 2, x: 200, y: 150, z: 100);
            feeder.Spawn(2, "PitchTarget", owner: 2, x: 120, y: 310, z: 50);
            feeder.Spawn(2, "PitchTarget", owner: 2, x: 140, y: 300, z: 11);
            world.Step();
            var flat = world.Entities.Single(value => value.Template.Name == "FlatShooter");
            var sky = world.Entities.Single(value => value.Template.Name == "SkyShooter");
            var targets = world.Entities.Where(value => value.Template.Name == "PitchTarget").OrderBy(value => value.Id.Value).ToArray();

            Check.That(world.ChooseWeapon(flat, targets[0], FireOrigin.Player).CanFire, $"{rate}: on level ground the pitch is no question.");
            Check.That(world.ChooseWeapon(sky, targets[1], FireOrigin.Player).CanFire, $"{rate}: \"always good enough if dz is small, regardless of pitch\".");
            Check.That(world.ChooseWeapon(flat, targets[2], FireOrigin.Player).Rejection == CommandRejection.InvalidWeaponTarget, $"{rate}: 42 degrees up is outside -15 to 15.");
            Check.That(world.ChooseWeapon(sky, targets[3], FireOrigin.Player).CanFire, $"{rate}: 66 degrees up is inside 45 to 80.");
            Check.That(world.ChooseWeapon(sky, targets[4], FireOrigin.Player).Rejection == CommandRejection.InvalidWeaponTarget, $"{rate}: 15 degrees up is below 45.");
        }
    }

    public static void TheDesignatorMarksItsTarget()
    {
        var bit = ObjectStatusBits.Index("FAERIE_FIRE");
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, fixture) = PitchWorld(rate);
            feeder.Spawn(1, "Designator", owner: 1, x: 100, y: 100);
            feeder.Spawn(1, "FlatShooter", owner: 1, x: 100, y: 200);
            feeder.Spawn(2, "PitchTarget", owner: 2, x: 200, y: 100);
            world.Step();
            var designator = world.Entities.Single(value => value.Template.Name == "Designator");
            var gun = world.Entities.Single(value => value.Template.Name == "FlatShooter");
            var target = world.Entities.Single(value => value.Template.Name == "PitchTarget");

            Check.That(feeder.FireWeapon(1, designator.Id.Value, target.Id.Value).Accepted, $"{rate}: the designator fires.");
            world.Step();
            Check.That(target.HasStatus(bit), $"{rate}: the target is marked.");
            Check.That(target.Body!.Health == target.Body.MaxHealth, $"{rate}: and not hurt.");

            Check.That(feeder.FireWeapon(1, gun.Id.Value, target.Id.Value).Accepted, $"{rate}: the gun fires at it.");
            world.Step();
            var condition = 1u << WeaponBonusTable.TargetFaerieFireCondition;
            Check.That((gun.WeaponBonusConditions & condition) != 0, $"{rate}: \"we shoot faster at guys marked thusly\".");

            var saved = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
            var marked = 0;
            var markedAfterSave = 0;
            for (var step = 0; step < world.Frames(30); step++)
            {
                world.Step();
                saved.Step();
                Check.That(world.StateHash == saved.StateHash, $"{rate}: a save keeps the mark, step {step}.");
                if (target.HasStatus(bit)) marked++;
                if (saved.FindEntity(target.Id)!.HasStatus(bit)) markedAfterSave++;
            }
            // 200 msec are six frames of the original from the tick of the hit; two ticks have gone and the last one clears it.
            Check.That(marked == world.Frames(6) - 3, $"{rate}: 200 msec of mark ({marked}).");
            Check.That(marked == markedAfterSave, $"{rate}: and it goes on the same tick after a save.");
        }
    }
}

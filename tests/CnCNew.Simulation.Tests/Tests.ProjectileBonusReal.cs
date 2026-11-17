using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #424, on the shipped data: a projectile's own weapon bonus counts at its detonation. The uranium shells and the AP rockets are <c>WeaponBonusUpgrade</c> modules on the shell and the
/// missile, not on the launcher, so the projectile carries <c>PLAYER_UPGRADE</c> from its birth (<c>Object::initObject</c>). <c>DumbProjectileBehavior::detonate</c> and
/// <c>MissileAIUpdate::detonate</c> fire the launcher's weapon with the projectile as the source, and <c>Weapon::computeBonus</c> takes the source's conditions and the launcher's of the
/// launch together: the weapon's <c>PLAYER_UPGRADE DAMAGE 125%</c> acts. The port dealt the damage with the launcher's conditions only (x1).
/// </summary>
static partial class Tests
{
    public static void RealProjectileCarriesItsOwnWeaponBonus(string directory)
    {
        var data = OpenRealFireData(directory);
        var cases = new[]
        {
            (Launcher: "ChinaTankBattleMaster", Upgrade: "Upgrade_ChinaUraniumShells", Projectile: "BattleMasterTankShell"),
            (Launcher: "GLAInfantryTunnelDefender", Upgrade: "Upgrade_GLAAPRockets", Projectile: "TunnelDefenderMissile"),
        };
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        foreach (var item in cases)
        {
            var hit = new long[2];
            var loss = new Fixed[2];
            for (var upgraded = 0; upgraded < 2; upgraded++)
            {
                var what = $"{rate} {item.Launcher}{(upgraded == 1 ? " with " + item.Upgrade : "")}";
                var world = NewRealFireWorld(data, rate, computer: false);
                ulong sequence = 0;
                var launcher = SpawnReal(world, ref sequence, item.Launcher, 1, 400, 400);
                var victim = SpawnReal(world, ref sequence, "AmericaVehicleDozer", 2, 500, 400);
                if (upgraded == 1) world.CompleteUpgrade(new PlayerId(1), null, item.Upgrade);
                for (var tick = 0; tick < world.Frames(2); tick++) world.Step();
                Check.That((launcher.WeaponBonusConditions & (1u << CnCNew.Simulation.Templates.WeaponBonusTable.PlayerUpgradeCondition)) == 0,
                    $"{what}: the upgrade is the projectile's, the launcher carries no PLAYER_UPGRADE.");
                var health = victim.Body!.Health;
                Check.That(world.Submit(new FireWeaponCommand(new PlayerId(1), ++sequence, world.Tick, launcher.Id, victim.Id)).Accepted, $"{what}: the fire order is accepted.");
                var start = world.Tick;
                var launched = false;
                var projectileUpgraded = false;
                while (victim.Body.Health == health)
                {
                    world.Step();
                    foreach (var entity in world.Entities)
                        if (entity.Template.Name == item.Projectile)
                        {
                            launched = true;
                            projectileUpgraded |= (entity.WeaponBonusConditions & (1u << CnCNew.Simulation.Templates.WeaponBonusTable.PlayerUpgradeCondition)) != 0;
                        }
                    Check.That(world.Tick - start < world.Frames(300), $"{what}: the dozer is hit within 10 s.");
                }
                Check.That(launched, $"{what}: the shot is a {item.Projectile}.");
                Check.That(projectileUpgraded == (upgraded == 1), $"{what}: the {item.Projectile} carries PLAYER_UPGRADE exactly when its player has the upgrade.");
                Check.That(victim.Body.Health > Fixed.Zero, $"{what}: the first hit does not kill the dozer.");
                hit[upgraded] = world.Tick - start;
                loss[upgraded] = health - victim.Body.Health;
            }
            Check.That(hit[0] == hit[1], $"{rate} {item.Launcher}: the shot lands at the same tick with and without the upgrade ({hit[0]}, {hit[1]}).");
            var ratio = loss[1] / loss[0];
            Check.That(ratio > Fixed.FromDecimal(1.249m) && ratio < Fixed.FromDecimal(1.251m),
                $"{rate} {item.Launcher}: {item.Upgrade} makes the {item.Projectile} do x1.25 ({loss[0]} -> {loss[1]}, x{ratio}).");
        }
    }

    /// <summary>
    /// #424, the launcher's part of a projectile's bonus: <c>DumbProjectileBehavior::projectileLaunchAtObjectOrPosition</c> and <c>MissileAIUpdate</c>'s keep the launcher's own
    /// <c>getWeaponBonusCondition()</c> as <c>m_extraBonusFlags</c>, not <c>Weapon::computeBonus</c>'s set - so the conditions a Battle Bus passes to its riders
    /// (<c>WeaponBonusPassedToPassengers</c>) count for a rider's immediate shot and not for its missile. On the shipped data a rebel in a veteran bus does x1.1 (<c>VETERAN DAMAGE 110%</c>), a
    /// tunnel defender's missile x1.
    /// </summary>
    public static void RealPassedBonusStaysOffAProjectile(string directory)
    {
        var data = OpenRealFireData(directory);
        var cases = new[]
        {
            (Rider: "GLAInfantryRebel", Expected: Fixed.FromDecimal(1.1m), What: "the rebel's rifle"),
            (Rider: "GLAInfantryTunnelDefender", Expected: Fixed.One, What: "the tunnel defender's missile"),
        };
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        foreach (var item in cases)
        {
            var loss = new Fixed[2];
            for (var veteran = 0; veteran < 2; veteran++)
            {
                var what = $"{rate} {item.Rider} in a{(veteran == 1 ? " veteran" : "")} Battle Bus";
                var world = NewRealFireWorld(data, rate, computer: false);
                ulong sequence = 0;
                var bus = SpawnReal(world, ref sequence, "GLAVehicleBattleBus", 1, 400, 400);
                var rider = SpawnReal(world, ref sequence, item.Rider, 1, 380, 400);
                Check.That(world.TryEnterContainer(rider, bus), $"{what}: the rider gets in.");
                var victim = SpawnReal(world, ref sequence, "AmericaVehicleDozer", 2, 480, 400);
                if (veteran == 1) world.SetVeterancy(bus.Id, 1);
                for (var tick = 0; tick < world.Frames(2); tick++) world.Step();
                Check.That(rider.ContainedBy == bus.Id, $"{what}: the rider rides in the bus.");
                var health = victim.Body!.Health;
                Check.That(world.Submit(new FireWeaponCommand(new PlayerId(1), ++sequence, world.Tick, rider.Id, victim.Id)).Accepted, $"{what}: the fire order is accepted.");
                var start = world.Tick;
                while (victim.Body.Health == health)
                {
                    world.Step();
                    Check.That(world.Tick - start < world.Frames(300), $"{what}: the dozer is hit within 10 s.");
                }
                Check.That(victim.Body.Health > Fixed.Zero, $"{what}: the first hit does not kill the dozer.");
                loss[veteran] = health - victim.Body.Health;
            }
            var ratio = loss[1] / loss[0];
            Check.That((ratio - item.Expected).Abs() < Fixed.FromDecimal(0.001m), $"{rate}: a veteran bus makes {item.What} do x{item.Expected} ({loss[0]} -> {loss[1]}, x{ratio}).");
        }
    }
}

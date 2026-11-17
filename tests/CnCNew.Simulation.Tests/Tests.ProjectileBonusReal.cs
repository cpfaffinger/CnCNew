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
}

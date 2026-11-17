using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Issue #429: three small differences to the original - an aura that starts active, an effect list on death behind an upgrade, what <c>isAbleToAttack</c> refuses.</summary>
static partial class Tests
{
    /// <summary>
    /// <c>AutoHealBehavior</c>'s constructor calls <c>UpgradeMux::giveSelfUpgrade</c> for <c>StartsActive</c>: the module's removals run first and its switch is marked done, so
    /// <c>isUpgradeActive</c> is the mux's executed flag. A later module's removal of the aura's trigger re-arms the aura (<c>resetUpgrade</c>), and it heals nothing until the upgrade
    /// comes; an aura that starts active and removes the trigger of an earlier death weapon switches that weapon off. The port set the aura active beside the mux: the removal did not
    /// reach it, and its own removals never ran. At 30, 60 and 120 Hz.
    /// </summary>
    public static void AnAuraThatStartsActiveGoesThroughItsUpgradeSwitch()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var files = new Dictionary<string, string>(Phase5Files())
            {
                ["Data/INI/Object/SelfAuraFixture.ini"] = """
                Object ResetAura
                  KindOf = SELECTABLE INFANTRY
                  Body = ActiveBody Body01
                    MaxHealth = 50.0
                    InitialHealth = 50.0
                  End
                  Behavior = DestroyDie Destroy01
                  End
                  Behavior = AutoHealBehavior Heal01
                    StartsActive = Yes
                    TriggeredBy = Upgrade_Detonator
                    HealingAmount = 5
                    HealingDelay = 100
                    AffectsWholePlayer = Yes
                  End
                  Behavior = FireWeaponWhenDeadBehavior FireDead01
                    DeathWeapon = AltSuicideWeapon
                    StartsActive = Yes
                    RemovesUpgrades = Upgrade_Detonator
                  End
                End
                Object RemovingAura
                  KindOf = SELECTABLE INFANTRY
                  Body = ActiveBody Body01
                    MaxHealth = 50.0
                    InitialHealth = 50.0
                  End
                  Behavior = DestroyDie Destroy01
                  End
                  Behavior = FireWeaponWhenDeadBehavior FireDead01
                    DeathWeapon = SuicideWeapon
                    StartsActive = Yes
                    TriggeredBy = Upgrade_AltDetonator
                  End
                  Behavior = AutoHealBehavior Heal01
                    StartsActive = Yes
                    RemovesUpgrades = Upgrade_AltDetonator
                    HealingAmount = 5
                    HealingDelay = 100
                    AffectsWholePlayer = Yes
                    SkipSelfForHealing = Yes
                  End
                End
                """,
            };
            var fixture = new SyntheticFixture(rate, files: files);

            // The aura whose trigger a later module removes is off: the damaged object is not healed. With the upgrade it heals again.
            Fixed HealthAfterDamage(bool detonator)
            {
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
                var feeder = new Feeder(world);
                feeder.Spawn(1, "ResetAura");
                world.Step();
                if (detonator)
                {
                    feeder.GrantUpgrade(1, "Upgrade_Detonator", target: 1);
                    world.Step();
                }
                feeder.Damage(1, 30);
                for (var step = 0; step < world.Frames(30); step++) world.Step();
                return world.FindEntity(new EntityId(1))!.Body!.Health;
            }
            var reset = HealthAfterDamage(detonator: false);
            Check.That(reset == Fixed.FromInt(20), $"{rate}: the aura a later module's removal re-armed heals nothing ({reset}).");
            var upgraded = HealthAfterDamage(detonator: true);
            Check.That(upgraded > Fixed.FromInt(20), $"{rate}: the upgrade that triggers it switches it on again ({upgraded}).");

            // The aura that starts active removes the trigger of the earlier death weapon: that weapon does not fire.
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "RemovingAura");
            world.Step();
            feeder.Kill(1);
            var fired = world.Step().Events.Where(value => value.Kind == SimEventKind.WeaponFireRequested).Select(value => value.Text).ToArray();
            Check.That(fired.Length == 0, $"{rate}: the aura's removal switched the earlier death weapon off ({string.Join(", ", fired)}).");
        }
    }
}

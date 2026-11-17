using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
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

    /// <summary>
    /// <c>FXListDie</c> is an upgrade module (<c>FXListDie : DieModule, UpgradeMux</c>): <c>StartsActive</c> (default yes) goes through <c>giveSelfUpgrade</c>, a <c>TriggeredBy</c>
    /// upgrade switches it on, and <c>onDie</c> plays only while the switch is on and neither the object nor its player has a <c>ConflictsWith</c> upgrade. The shipped anthrax bomb
    /// (<c>AnthraxBomb</c>, <c>AnthraxBombGamma</c>) chooses its effect list so: the plain one conflicts with Beta and Gamma, Beta's is triggered by Beta and conflicts with Gamma,
    /// Gamma's is triggered by Gamma. The port had no switch: the plain list always played, the triggered ones never. A save keeps the switch. At 30, 60 and 120 Hz.
    /// </summary>
    public static void AnEffectListOnDeathFollowsItsUpgradeSwitch()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var files = new Dictionary<string, string>(Phase5Files())
            {
                ["Data/INI/FXList.ini"] = "FXList FX_Test\nEnd\nFXList FX_Other\nEnd\nFXList FX_Plain429\nEnd\nFXList FX_Beta429\nEnd\nFXList FX_Gamma429\nEnd\n",
                ["Data/INI/Object/UpgradedBombFixture.ini"] = """
                Object UpgradedBomb
                  KindOf = SELECTABLE INFANTRY
                  Body = ActiveBody Body01
                    MaxHealth = 50.0
                    InitialHealth = 50.0
                  End
                  Behavior = DestroyDie Destroy01
                  End
                  Behavior = FXListDie Plain01
                    DeathFX = FX_Plain429
                    StartsActive = Yes
                    ConflictsWith = Upgrade_Detonator Upgrade_AltDetonator
                  End
                  Behavior = FXListDie Beta01
                    DeathFX = FX_Beta429
                    StartsActive = No
                    TriggeredBy = Upgrade_Detonator
                    ConflictsWith = Upgrade_AltDetonator
                  End
                  Behavior = FXListDie Gamma01
                    DeathFX = FX_Gamma429
                    StartsActive = No
                    TriggeredBy = Upgrade_AltDetonator
                  End
                End
                """,
            };
            var fixture = new SyntheticFixture(rate, files: files);
            string DeathEffects(bool objectUpgrade, bool playerUpgrade, bool restore)
            {
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
                var feeder = new Feeder(world);
                if (playerUpgrade) feeder.GrantUpgrade(1, "Upgrade_AltDetonator");
                feeder.Spawn(1, "UpgradedBomb");
                world.Step();
                if (objectUpgrade)
                {
                    feeder.GrantUpgrade(1, "Upgrade_Detonator", target: 1);
                    world.Step();
                }
                if (restore)
                {
                    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
                    Check.That(restored.StateHash == world.StateHash, $"{rate}: a restored world has the same hash.");
                    world = restored;
                    feeder = feeder.ForkTo(world);
                }
                feeder.Kill(1);
                return string.Join(", ", world.Step().Events.Where(value => value.Kind == SimEventKind.FxRequested).Select(value => value.Text));
            }
            foreach (var restore in new[] { false, true })
            {
                var plain = DeathEffects(objectUpgrade: false, playerUpgrade: false, restore);
                Check.That(plain == "FX_Plain429", $"{rate}: without an upgrade only the plain list plays ({plain}).");
                var beta = DeathEffects(objectUpgrade: true, playerUpgrade: false, restore);
                Check.That(beta == "FX_Beta429", $"{rate}: the object's upgrade switches Beta's list on and the plain one conflicts ({beta}, restored {restore}).");
                var gamma = DeathEffects(objectUpgrade: false, playerUpgrade: true, restore);
                Check.That(gamma == "FX_Gamma429", $"{rate}: the player's upgrade switches Gamma's list on, the others conflict ({gamma}, restored {restore}).");
                var both = DeathEffects(objectUpgrade: true, playerUpgrade: true, restore);
                Check.That(both == "FX_Gamma429", $"{rate}: with both, Beta's list conflicts with the player's upgrade ({both}, restored {restore}).");
            }
        }
    }
}

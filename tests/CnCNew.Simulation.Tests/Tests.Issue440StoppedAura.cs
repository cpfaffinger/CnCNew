using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Issue #440: a healing aura that a mine stopped (<c>AutoHealBehavior::stopHealing</c>) stays stopped.</summary>
static partial class Tests
{
    private const string StoppedAura440Ini = """
        Object StoppedAuraMine440
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AutoHealBehavior Heal01
            TriggeredBy = Upgrade_Detonator
            HealingAmount = 2
            HealingDelay = 1000
          End
        End
        """;

    /// <summary>
    /// <c>AutoHealBehavior::stopHealing</c> sets <c>m_stopped</c> for good: <c>update</c>, <c>onDamage</c> and <c>pulseHealObject</c> of a stopped aura do nothing, so an aura whose
    /// upgrade is taken away and given again after it was stopped heals nothing. The port only switched it off, and the upgrade switched it on again. The stop is kept across a
    /// save. At 30, 60 and 120 Hz.
    /// </summary>
    public static void AStoppedHealingAuraStaysStopped()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var files = new Dictionary<string, string>(Phase5Files()) { ["Data/INI/Object/StoppedAuraFixture440.ini"] = StoppedAura440Ini };
            var fixture = new SyntheticFixture(rate, files: files);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates, null);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "StoppedAuraMine440", owner: 1, x: 0, y: 0);
            StepFrames(world, 1);
            var mine = HealAura438Find(world, "StoppedAuraMine440");
            feeder.GrantUpgrade(1, "Upgrade_Detonator", target: mine.Id.Value);
            StepFrames(world, 1);
            feeder.Damage(mine.Id.Value, 50);
            var (healing, _) = HealAura438Heals(world, mine, 90);
            Check.That(healing.Count >= 2, $"{rate}: the switched-on aura heals its mine ({healing.Count} heals).");

            mine.Modules.OfType<AutoHealBehavior>().Single().StopHealing(world);
            feeder.Damage(mine.Id.Value, 30);
            StepFrames(world, 1);
            world.RemoveUpgradeByName(mine, "Upgrade_Detonator");
            StepFrames(world, 1);
            feeder.GrantUpgrade(1, "Upgrade_Detonator", target: mine.Id.Value);
            StepFrames(world, 1);
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, null);
            Check.That(restored.StateHash == world.StateHash, $"{rate}: a restored world has the same hash.");
            var (ticks, _) = HealAura438Heals(world, mine, 300);
            var (restoredTicks, _) = HealAura438Heals(restored, restored.FindEntity(mine.Id)!, 300);
            Check.That(ticks.Count == 0, $"{rate}: the stopped aura heals nothing when its upgrade comes again ({ticks.Count} heals).");
            Check.That(restoredTicks.Count == 0 && restored.StateHash == world.StateHash, $"{rate}: nor in the restored world ({restoredTicks.Count} heals).");
        }
    }
}

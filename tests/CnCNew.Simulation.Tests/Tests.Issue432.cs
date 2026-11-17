using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Issue #432: when the self-heal of <c>AutoHealBehavior</c> (Junk Repair, <c>Radius = 0</c>) heals first, and every time after.</summary>
static partial class Tests
{
    private const string SelfHealIni = """
        Object JunkCar432
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AutoHealBehavior Heal01
            HealingAmount = 2
            HealingDelay = 1000
            TriggeredBy = Upgrade_Detonator
          End
        End
        Object WaitingCar432
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 2
            HealingDelay = 1000
            StartHealingDelay = 2000
          End
        End
        Object AuraHealer432
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 2
            HealingDelay = 1000
            KindOf = VEHICLE
            AffectsWholePlayer = Yes
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) SelfHealWorld(SimulationRate rate)
    {
        var files = new Dictionary<string, string>(Phase5Files()) { ["Data/INI/Object/SelfHealFixture432.ini"] = SelfHealIni };
        var fixture = new SyntheticFixture(rate, files: files);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world), fixture);
    }

    /// <summary>Steps until the object's health rises and returns the tick that raised it, or -1 within <paramref name="frames"/> frames of the original.</summary>
    private static long NextSelfHeal(SimulationWorld world, int frames)
    {
        var entity = world.FindEntity(new EntityId(1))!;
        var health = entity.Body!.Health;
        for (var step = 0; step < world.Frames(frames); step++)
        {
            world.Step();
            if (entity.Body!.Health > health) return world.Tick - 1;
            health = entity.Body.Health;
        }
        return -1;
    }

    /// <summary>
    /// <c>AutoHealBehavior::onDamage</c> and <c>update</c> for <c>Radius = 0</c>: at full health the self-heal sleeps, a hit wakes it with the next frame (the first heal one frame
    /// after the hurt), it heals every <c>HealingDelay</c> while hurt and sleeps again at full health; a hit before the next pulse is due (<c>m_soonestHealFrame</c>, kept across a
    /// save) does not wake it early. The port pulsed on a timer from the upgrade whatever the health, so the first heal came anywhere up to a second after the hurt - at 30 Hz a
    /// whole second, at 60 and 120 Hz at once in the faction buff measurement of #419. At 30, 60 and 120 Hz.
    /// </summary>
    public static void ASelfHealWakesOnTheHitAndSleepsAtFullHealth()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder, fixture) = SelfHealWorld(rate);
            feeder.Spawn(1, "JunkCar432", owner: 1);
            StepFrames(world, 1);
            feeder.GrantUpgrade(1, "Upgrade_Detonator", target: 1);
            StepFrames(world, 6);
            var entity = world.FindEntity(new EntityId(1))!;
            var module = entity.Modules.OfType<AutoHealBehavior>().Single();
            Check.That(module.NextWake >= SimulationTiming.SleepForever, $"{rate}: at full health the self-heal sleeps ({module.NextWake}).");

            feeder.Damage(1, 30);
            var hurt = world.Tick;
            var first = NextSelfHeal(world, 40);
            Check.That(first - hurt == world.Frames(1), $"{rate}: the first heal comes one frame after the hurt ({first - hurt} ticks).");

            // Ten frames on a second hit: the next pulse is not due, so it does not come early - also in a restored world.
            StepFrames(world, 9);
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
            Check.That(restored.StateHash == world.StateHash, $"{rate}: a restored world has the same hash.");
            foreach (var side in new[] { world, restored })
            {
                var sideFeeder = feeder.ForkTo(side);
                sideFeeder.Damage(1, 10);
                var second = NextSelfHeal(side, 40);
                Check.That(second - first == side.Frames(30), $"{rate}: a hit before the pulse is due waits for it ({second - first} ticks after the first heal).");
            }

            // 40 damage in all at 2 a pulse: 20 heals, a second apart, and then the module sleeps.
            var heals = new List<long> { first, first + world.Frames(30) };
            for (var heal = NextSelfHeal(world, 40); heal >= 0; heal = NextSelfHeal(world, 40)) heals.Add(heal);
            Check.That(entity.Body!.Health == entity.Body.MaxHealth, $"{rate}: the self-heal brings the object to full health ({entity.Body.Health}).");
            Check.That(heals.Zip(heals.Skip(1), (a, b) => b - a).All(value => value == world.Frames(30)), $"{rate}: a heal every HealingDelay ({string.Join(" ", heals.Zip(heals.Skip(1), (a, b) => b - a))}).");
            Check.That(heals.Count == 20, $"{rate}: 20 heals of 2 ({heals.Count}).");
            Check.That(module.NextWake >= SimulationTiming.SleepForever, $"{rate}: at full health it sleeps again ({module.NextWake}).");
        }
    }

    /// <summary>
    /// <c>AutoHealBehavior::onDamage</c> with a <c>StartHealingDelay</c>: every hit puts the self-heal to sleep for that long, so it heals first that long after the last hit, then
    /// every <c>HealingDelay</c>. The port started the delay at its birth and pulsed on a timer from there. At 30, 60 and 120 Hz.
    /// </summary>
    public static void ASelfHealWaitsItsStartDelayAfterTheLastHit()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder, _) = SelfHealWorld(rate);
            feeder.Spawn(1, "WaitingCar432", owner: 1);
            StepFrames(world, 90);
            feeder.Damage(1, 30);
            StepFrames(world, 30);
            feeder.Damage(1, 10);
            var hurt = world.Tick;
            var first = NextSelfHeal(world, 90);
            Check.That(first - hurt == world.Frames(60), $"{rate}: the first heal comes StartHealingDelay after the last hit ({first - hurt} ticks).");
            var second = NextSelfHeal(world, 40);
            Check.That(second - first == world.Frames(30), $"{rate}: then every HealingDelay ({second - first} ticks).");
        }
    }

    /// <summary>
    /// The constructor of <c>AutoHealBehavior</c>: an aura that starts active first wakes <c>GameLogicRandomValue(1, HealingDelay)</c> frames after it appears ("random phasings so
    /// that we don't have all of 'em check on the same frame"), one draw of the logic stream after <c>giveSelfUpgrade</c>; an aura of an upgrade draws nothing. The port woke it
    /// <c>StartHealingDelay</c> (at least a frame) after its birth without a draw. The same draw, so the same frame, at 30, 60 and 120 Hz.
    /// </summary>
    public static void AnAuraThatStartsActiveFirstWakesAtARandomPhase()
    {
        var drawn = new List<long>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder, _) = SelfHealWorld(rate);
            feeder.Spawn(1, "JunkCar432", owner: 1);
            StepFrames(world, 1);
            feeder.Damage(1, 30);
            StepFrames(world, 1);
            var state = new uint[6];
            world.LogicRandom.CopyState(state);
            var expected = new CnCNew.Simulation.Random.SageRandom(0);
            expected.SetState(state);
            var frames = expected.Range(1, 30);
            var appeared = world.Tick;
            feeder.Spawn(1, "AuraHealer432", owner: 1);
            world.Step();
            var after = new uint[6];
            world.LogicRandom.CopyState(after);
            var stateAfterOneDraw = new uint[6];
            expected.CopyState(stateAfterOneDraw);
            Check.That(after.SequenceEqual(stateAfterOneDraw), $"{rate}: the healer's birth draws once from the logic stream.");
            var healer = world.Entities.Single(entity => entity.Template.Name == "AuraHealer432");
            var module = healer.Modules.OfType<AutoHealBehavior>().Single();
            Check.That(module.NextWake == appeared + world.Frames(frames), $"{rate}: the aura first wakes the drawn {frames} frames after its birth ({module.NextWake - appeared} ticks).");
            var heal = NextSelfHeal(world, 40);
            Check.That(heal == appeared + world.Frames(frames), $"{rate}: and heals the hurt car then ({heal - appeared} ticks).");
            drawn.Add(frames);
        }
        Check.That(drawn.Distinct().Count() == 1, $"The same draw at every rate ({string.Join(" ", drawn)}).");
    }
}

using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 20, stage 2, modules A–M: what was a bare tick number, a rhythm or a rounding in ticks is now written in logic frames of the original. Each
/// test runs the same scene at 30, 60 and 120 Hz and requires the tick counts at the higher rates to be exactly 2× and 4× the 30 Hz ones – the same
/// frame of the original fires. (That 30 Hz itself is unchanged is what the existing module tests and the reference manifest prove.)
/// </summary>
static partial class Tests
{
    private static readonly (SimulationRate Rate, int Factor)[] ModulesAmRates = [(SimulationRate.Hz30, 1), (SimulationRate.Hz60, 2), (SimulationRate.Hz120, 4)];

    private static SimulationWorld ModulesAmWorld(SimulationRate rate, IReadOnlyDictionary<string, string> files, out SyntheticFixture fixture, out Feeder feeder, TerrainMap? terrain = null)
    {
        fixture = new SyntheticFixture(rate, files: files);
        var world = terrain is null
            ? new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates)
            : new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        feeder = new Feeder(world);
        return world;
    }

    private static void RequireScaled(IReadOnlyList<long> thirty, IReadOnlyList<long> other, int factor, string what)
        => Check.That(thirty.Count > 0 && other.Count == thirty.Count && thirty.Select(value => value * factor).SequenceEqual(other),
            $"{what}: at {30 * factor} Hz expected {string.Join(",", thirty.Select(value => value * factor))} but got {string.Join(",", other)}.");

    /// <summary>
    /// <c>BaseRegenerateUpdate</c> (<c>HEAL_RATE = 3</c>, class A): the delay, the burst rhythm (every three frames of the original) and the amount of a
    /// burst (that many ticks' worth) are the same game time and the same health at every rate.
    /// </summary>
    public static void TickRateModulesAmBaseRegenerate()
    {
        var files = new Dictionary<string, string>(Phase5Files())
        {
            ["Data/INI/GameData.ini"] = "GameData\n  BaseRegenHealthPercentPerSecond = 12.5%\n  BaseRegenDelay = 1000\nEnd\n",
            ["Data/INI/Object/TickRateRegen.ini"] = """
            Object TrRegenerator
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 1000.0
                InitialHealth = 1000.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = BaseRegenerateUpdate Regen01
              End
            End
            """,
        };
        List<long>? thirty = null;
        foreach (var (rate, factor) in ModulesAmRates)
        {
            var world = ModulesAmWorld(rate, files, out _, out var feeder);
            feeder.Spawn(1, "TrRegenerator");
            world.Step();
            while (world.Tick < 4 * factor) world.Step();
            feeder.Damage(1, 300);
            world.Step();
            var hitTick = world.Tick - 1;
            var body = world.FindEntity(new EntityId(1))!.Body!;
            var bursts = new List<long>();
            var health = body.Health;
            while (world.Tick <= hitTick + 50 * factor)
            {
                world.Step();
                if (body.Health == health) continue;
                // 1000 · 12.5 % · (3 frames) / (30 frames per second) = 12.5 per burst at every rate (exact in fixed point).
                Check.That(body.Health - health == Fixed.FromDecimal(12.5m), $"{rate}: a burst heals three frames' worth ({body.Health - health}).");
                health = body.Health;
                bursts.Add(world.Tick - 1 - hitTick);
            }
            if (thirty is null)
            {
                thirty = bursts;
                Check.That(bursts.Take(3).SequenceEqual([30L, 33L, 36L]), $"30 Hz: the first bursts come 30, 33 and 36 ticks after the hit ({string.Join(",", bursts)}).");
            }
            else RequireScaled(thirty, bursts, factor, "BaseRegenerateUpdate bursts");
        }
    }

    /// <summary>
    /// <c>LifetimeUpdate</c> (class W1, a draw over a range of durations) and <c>AutoDepositUpdate</c> (a one-tick minimum that is one frame of the
    /// original): the lifetime is drawn in frames of the original, so the same draw gives the same game time; a deposit rhythm of 0 ms pays once per
    /// frame of the original, and a rhythm from the data stays on the grid.
    /// </summary>
    public static void TickRateModulesAmLifetimeAndDeposit()
    {
        var files = new Dictionary<string, string>(SupplyFiles())
        {
            ["Data/INI/Object/TickRateLifetime.ini"] = """
            Object TrMayfly
              KindOf = SELECTABLE
              Body = ActiveBody Body01
                MaxHealth = 10.0
                InitialHealth = 10.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = LifetimeUpdate Life01
                MinLifetime = 333
                MaxLifetime = 4777
              End
            End

            Object TrPayEveryFrame
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = AutoDepositUpdate Pay01
                DepositTiming = 0
                DepositAmount = 1
              End
            End
            """,
        };
        List<long>? lifetimes = null;
        List<long>? paid = null;
        foreach (var (rate, factor) in ModulesAmRates)
        {
            var world = ModulesAmWorld(rate, files, out _, out var feeder);
            for (var index = 0; index < 12; index++) feeder.Spawn(1, "TrMayfly", x: 10 * index);
            world.Step();
            var drawn = world.Entities.Where(value => value.Template.Name == "TrMayfly").OrderBy(value => value.Id.Value)
                .Select(value => value.Modules.OfType<TimedExpiryModule>().Single().DieTick).ToList();
            Check.That(drawn.Count == 12 && drawn.Distinct().Count() > 3, $"{rate}: twelve lifetimes were drawn.");
            if (lifetimes is null)
            {
                lifetimes = drawn;
                // 333 ms = 10 frames, 4777 ms = 144 frames.
                Check.That(drawn.All(value => value is >= 10 and <= 144), $"30 Hz: every lifetime lies in 10..144 ticks ({string.Join(",", drawn)}).");
            }
            else RequireScaled(lifetimes, drawn, factor, "LifetimeUpdate draws");

            // The periodic post of the supply fixture (1000 ms) and the one that pays "every frame".
            var deposits = ModulesAmWorld(rate, files, out _, out var payer);
            payer.Spawn(1, "PayPost");
            payer.Spawn(2, "TrPayEveryFrame", x: 200);
            var periodic = new List<long>();
            var everyFrame = new List<long>();
            while (deposits.Tick < 95 * factor)
                foreach (var deposit in deposits.Step().Events.Where(value => value.Kind == SimEventKind.SupplyDeposited))
                    (deposit.Value == 40 ? periodic : everyFrame).Add(deposits.Tick - 1);
            Check.That(periodic.SequenceEqual([30L * factor, 60L * factor, 90L * factor]), $"{rate}: the 1000 ms post pays at {string.Join(",", periodic)}.");
            Check.That(everyFrame.Zip(everyFrame.Skip(1), (first, second) => second - first).All(gap => gap == factor), $"{rate}: a deposit rhythm of 0 ms pays once per frame of the original.");
            var money = deposits.MoneyOf(new PlayerId(2));
            if (paid is null) paid = [everyFrame.Count];
            else Check.That(System.Math.Abs(everyFrame.Count - paid[0]) <= 1, $"{rate}: {everyFrame.Count} payments in the same game time as {paid[0]} at 30 Hz ({money}).");
        }
    }

    /// <summary>
    /// <c>HackInternetAIUpdate</c> (class W1: a countdown with period N + 1 and a varied, floored duration): unpacking and every payout fall on the same
    /// frame of the original at every rate – the hacker's income per second does not depend on the tick rate.
    /// </summary>
    public static void TickRateModulesAmHackerCountdown()
    {
        List<long>? thirty = null;
        foreach (var (rate, factor) in ModulesAmRates)
        {
            var world = ModulesAmWorld(rate, SlaveFiles(), out _, out var feeder);
            feeder.Spawn(1, "SvHackCenter", x: 100, y: 100);
            feeder.Spawn(1, "SvHacker", x: 100, y: 100);
            while (world.Tick < 2 * factor) world.Step();
            var center = SlaveEntity(world, "SvHackCenter");
            var hacker = SlaveEntity(world, "SvHacker");
            Check.That(world.TryEnterContainer(hacker, center), $"{rate}: the hacker enters.");
            var start = world.Tick;
            var payouts = new List<long>();
            var money = world.MoneyOf(new PlayerId(1));
            while (world.Tick < start + 100 * factor)
            {
                world.Step();
                if (world.MoneyOf(new PlayerId(1)) == money) continue;
                money = world.MoneyOf(new PlayerId(1));
                payouts.Add(world.Tick - 1 - start);
            }
            if (thirty is null)
            {
                thirty = payouts;
                // CashUpdateDelayFast 500 ms = 15 frames, one update more for the payout itself.
                Check.That(payouts.Count >= 4 && payouts.Zip(payouts.Skip(1), (first, second) => second - first).All(gap => gap == 16), $"30 Hz: a payout every 16th tick ({string.Join(",", payouts)}).");
            }
            else
            {
                // The order to hack arrives between two steps and the first countdown starts in the update of that very tick; since phase 23 a phase started by
                // an order lasts exactly its frames, so the first payout falls on the 30 Hz frame (it used to come up to factor − 1 ticks later).
                var offset = payouts[0] - thirty[0] * factor;
                Check.That(offset == 0, $"{rate}: the first payout is {offset} ticks off the 30 Hz frame.");
                RequireScaled(thirty, payouts.Select(value => value - offset).ToList(), factor, "HackInternetAIUpdate payouts");
            }
        }
    }

    /// <summary>
    /// <c>TensileFormationUpdate</c> (class A and C: <c>UPDATE_SLEEP(30)</c>, <c>++m_life &gt; 300</c>, <c>m_life % 30 == 29</c>): the dormant check and the
    /// length of the slide are frames of the original. (The slide itself is per-tick integration and belongs to phase 23.)
    /// </summary>
    public static void TickRateModulesAmTensileFormation()
    {
        long enabledThirty = -1;
        foreach (var (rate, factor) in ModulesAmRates)
        {
            var map = SyntheticMaps.Build(60, 40, 1, height: (x, _) => x);
            var terrain = TerrainMap.Build(map, "slope");
            var world = ModulesAmWorld(rate, SlaveFiles(), out _, out var feeder, terrain);
            feeder.Spawn(1, "SvRock", x: 300, y: 200);
            feeder.Spawn(1, "SvRock", x: 310, y: 200);
            while (world.Tick < 100 * factor) world.Step();
            var rocks = world.Entities.Where(value => value.Template.Name == "SvRock").OrderBy(value => value.Id.Value).ToArray();
            var first = ModuleOf<TensileFormationUpdate>(rocks[0]);
            Check.That(!first.Enabled, $"{rate}: an undamaged formation sits still.");
            feeder.Damage(rocks[0].Id.Value, 60);
            while (!first.Enabled && world.Tick < 200 * factor) world.Step();
            // The first update runs one frame of the original after the spawn at tick 0 (phase 23; one tick at 30 Hz); from there the dormant check recurs every
            // 30 frames of the original.
            var enabled = world.Tick - 1 - factor;
            if (enabledThirty < 0)
            {
                enabledThirty = enabled;
                Check.That(enabled == 120, $"30 Hz: the hurt rock notices at its check 120 ticks after its first update ({enabled}).");
            }
            else Check.That(enabled == enabledThirty * factor, $"{rate}: the dormant check fell on tick {enabled} after the first update, expected {enabledThirty * factor}.");
            while (world.Tick < 600 * factor) world.Step();
            Check.That(first.Life == 300 * factor + 1, $"{rate}: the slide lasts 300 frames of the original (life {first.Life}).");
        }
    }
}

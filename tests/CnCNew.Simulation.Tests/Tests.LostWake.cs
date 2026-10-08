using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The lost wake request (<c>docs/DEFECT-LOST-WAKE.md</c>). <c>SimulationWorld.WakeAt</c> drops, as the original's <c>GameLogic::friend_awakenUpdateModule</c> does, a
/// request for the next tick when the module is still due in the running tick: that module's own update, called in this tick, decides with its return value. A slow
/// death of one frame that begins in such a tick therefore has to carry the rest of its timer in the return value of the update - it used to return "forever", and
/// the dead object stayed in the world. The module is due in the tick after its object appeared (every module is called once there), and the battle bus is due
/// whenever it looks at its hulk.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> LostWakeFiles() => new Dictionary<string, string>(Phase5Files())
    {
        ["Data/INI/Object/LostWakeFixture.ini"] = """
        Object LwFlash
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = LifetimeUpdate Life01
            MinLifetime = 1
            MaxLifetime = 1
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 1
          End
        End

        Object LwGhost
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 1
          End
        End

        Object LwBus
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = TransportContain Transport01
            Slots = 4
            AllowInsideKindOf = INFANTRY
          End
          Behavior = BattleBusSlowDeathBehavior Bus01
            DeathTypes = ALL -EXTRA_4
            EmptyHulkDestructionDelay = 1000
            DestructionDelay = 1
          End
        End
        """,
    };

    private static SimulationWorld LostWakeWorld(SimulationRate rate, out Feeder feeder)
    {
        var fixture = new SyntheticFixture(rate, files: LostWakeFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    /// <summary>Steps until the object has left the world and returns the ticks from <paramref name="sinceTick"/> to the tick it was removed in; -1 after two seconds.</summary>
    private static long LostWakeTicksUntilGone(SimulationWorld world, string template, long sinceTick)
    {
        var limit = world.Time.WholeSecondsToTicks(2);
        for (var step = 0L; step < limit; step++)
        {
            var tick = world.Step().Tick;
            if (world.Entities.All(value => value.Template.Name != template)) return tick - sinceTick;
        }
        return -1;
    }

    private static void LostWakeSame(string what, long expectedFrames, System.Func<SimulationRate, long> measureTicks)
    {
        foreach (var rate in NzRates)
        {
            var ticks = measureTicks(rate);
            Check.That(ticks == expectedFrames * rate.TicksPerOriginalFrame,
                $"{rate}: {what} takes {expectedFrames} frames of the original ({expectedFrames * rate.TicksPerOriginalFrame} ticks), measured {ticks} (-1: the object never left the world).");
        }
    }

    /// <summary>
    /// The object of the retail data that showed it (<c>CINE_BShipWeaponDetonationObject</c>): a lifetime of one frame and a slow death of one frame. The original's
    /// <c>LifetimeUpdate</c> kills it one frame after its creation, <c>SlowDeathBehavior::beginSlowDeath</c> sets the destruction frame one frame later and wakes the
    /// module (asleep since its constructor) for it: the object is destroyed two frames after its creation.
    /// </summary>
    public static void LostWakeALifetimeAndASlowDeathOfOneFrameEnd()
    {
        LostWakeSame("a lifetime of one frame and a slow death of one frame: creation to removal", 2, rate =>
        {
            var world = LostWakeWorld(rate, out var feeder);
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var created = world.Tick;
            Check.That(feeder.Spawn(1, "LwFlash").Accepted, "The spawn is accepted.");
            return LostWakeTicksUntilGone(world, "LwFlash", created);
        });
    }

    /// <summary>
    /// The same hole without a lifetime: the object is killed from outside in the tick after it appeared, before its modules have been called for the first time.
    /// The slow death of one frame ends one frame after the kill.
    /// </summary>
    public static void LostWakeASlowDeathOfOneFrameBegunOnTheFirstFrameEnds()
    {
        foreach (var template in new[] { "LwGhost", "LwBus" })
        {
            LostWakeSame($"{template}, killed in the tick after it appeared: kill to removal", 1, rate =>
            {
                var world = LostWakeWorld(rate, out var feeder);
                Check.That(feeder.Spawn(1, template).Accepted, "The spawn is accepted.");
                world.Step();
                var victim = world.Entities.Single(value => value.Template.Name == template);
                var killed = world.Tick;
                Check.That(feeder.Kill(victim.Id.Value).Accepted, "The kill is accepted.");
                var ticks = LostWakeTicksUntilGone(world, template, killed);
                Check.That(victim.SlowDeathStarted, $"{rate}: {template} died a slow death.");
                return ticks;
            });
        }
    }

    /// <summary>
    /// The battle bus later in its life: it looks at its hulk every 15 frames, and a real death of one frame that begins in a tick of such a look, before the look,
    /// is the same case. Killed on every tick of a second in turn, the bus is always removed one frame after the kill.
    /// </summary>
    public static void LostWakeABattleBusKilledWhileItLooksAtItsHulkEnds()
    {
        foreach (var rate in NzRates)
        {
            var second = rate.Time.WholeSecondsToTicks(1);
            for (var wait = 0L; wait < second; wait++)
            {
                var world = LostWakeWorld(rate, out var feeder);
                Check.That(feeder.Spawn(1, "LwBus").Accepted, "The spawn is accepted.");
                world.Step();
                for (var step = 0L; step < wait; step++) world.Step();
                var bus = world.Entities.Single(value => value.Template.Name == "LwBus");
                var killed = world.Tick;
                Check.That(feeder.Kill(bus.Id.Value).Accepted, "The kill is accepted.");
                var ticks = LostWakeTicksUntilGone(world, "LwBus", killed);
                Check.That(ticks == rate.TicksPerOriginalFrame,
                    $"{rate}: a bus killed {wait.ToString(CultureInfo.InvariantCulture)} ticks after its first tick is removed one frame of the original later, measured {ticks.ToString(CultureInfo.InvariantCulture)} ticks (-1: never).");
            }
        }
    }

    /// <summary>
    /// Census over the retail data: every instantiable object with a <c>SlowDeathBehavior</c> or a <c>BattleBusSlowDeathBehavior</c>, once left alone, once killed in
    /// the tick after it appeared, and once killed there and again in the next tick (the battle bus survives the first kill with its second life and is thrown; the
    /// second kill is its real death, in a tick its module is due in). An object whose slow death has begun must have left the world when the longest destruction
    /// delay of its data has passed.
    /// </summary>
    public static void RealLostWakeCensus(string directory)
    {
        var catalogs = RateCatalogsOf(directory);
        foreach (var rate in NzRates)
        {
            var catalog = catalogs.Of(rate);
            var names = catalog.Templates
                .Where(template => template.IsInstantiable && template.Modules.Any(module => module.Data is SlowDeathData or BattleBusData))
                .Select(template => template.Name).OrderBy(name => name, System.StringComparer.Ordinal).ToArray();
            Check.That(names.Length > 500, $"{rate}: the retail data has many objects with a slow death ({names.Length}).");
            var stuckAlone = new List<string>();
            var stuckKilled = new List<string>();
            var stuckKilledTwice = new List<string>();
            var oneFrame = 0;
            foreach (var name in names)
            {
                Check.That(catalog.TryGet(name, out var template), name);
                var delays = template.Modules.Select(module => module.Data switch
                {
                    SlowDeathData slow => slow.DestructionDelayTicks + slow.DestructionDelayVarianceTicks,
                    BattleBusData bus => bus.Base.DestructionDelayTicks + bus.Base.DestructionDelayVarianceTicks,
                    _ => -1,
                }).Where(value => value >= 0).ToArray();
                if (delays.Min() <= rate.TicksPerOriginalFrame) oneFrame++;
                var budget = delays.Max() + rate.Time.WholeSecondsToTicks(5);
                if (LostWakeStuck(catalogs, rate, name, budget, kills: 0)) stuckAlone.Add(name);
                if (LostWakeStuck(catalogs, rate, name, budget, kills: 1)) stuckKilled.Add(name);
                if (LostWakeStuck(catalogs, rate, name, budget, kills: 2)) stuckKilledTwice.Add(name);
            }
            System.Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"       lost wake census, {rate}: {names.Length} objects with a slow death, {oneFrame} of them with a destruction delay of at most one frame; dead and never removed: " +
                $"{stuckAlone.Count} left alone [{string.Join(", ", stuckAlone)}], {stuckKilled.Count} killed in the tick after they appeared [{string.Join(", ", stuckKilled)}], " +
                $"{stuckKilledTwice.Count} killed there and again in the next tick [{string.Join(", ", stuckKilledTwice)}]"));
            Check.That(stuckAlone.Count == 0 && stuckKilled.Count == 0 && stuckKilledTwice.Count == 0,
                $"{rate}: {stuckAlone.Count} objects left alone, {stuckKilled.Count} killed in the tick after they appeared and {stuckKilledTwice.Count} killed twice began a slow death and never left the world.");
        }
    }

    /// <summary>Whether the object began a slow death and is still in the world <paramref name="budget"/> ticks later.</summary>
    private static bool LostWakeStuck(RateCatalogs catalogs, SimulationRate rate, string name, long budget, int kills)
    {
        var scene = new RateScene(catalogs, rate);
        var world = scene.World;
        var start = world.Tick;
        scene.Order(1, (owner, sequence, now) => new SpawnCommand(owner, sequence, now, name, owner, Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero));
        scene.Step();
        var thing = scene.Find(name, start);
        if (thing is null) return false;
        for (var kill = 0; kill < kills; kill++)
        {
            if (kill > 0) scene.Step();
            scene.Send(0, (_, sequence, now) => new KillCommand(PlayerId.System, sequence, now, thing.Id));
        }
        // Left alone, an object gets ten seconds to begin dying by itself (a lifetime); most never do.
        var begin = rate.Time.WholeSecondsToTicks(10);
        for (var step = 0L; step < begin && !thing.SlowDeathStarted && !scene.Gone(thing); step++) scene.Step();
        if (!thing.SlowDeathStarted) return false;
        for (var step = 0L; step <= budget && !scene.Gone(thing); step++) scene.Step();
        return !scene.Gone(thing);
    }
}

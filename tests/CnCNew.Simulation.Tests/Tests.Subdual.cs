using System.Linq;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// <c>ActiveBody::attemptDamage</c>, <c>SubdualDamageHelper</c>: the microwave tank's and the ECM tank's damage. It leaves the health alone and disables what holds as much of it as it has
/// health; it wears off a step at a time, and every hit puts the next step off. Before, every subdual weapon did nothing at all.
/// </summary>
static partial class Tests
{
    private const string SubdualIni = """
        Object Subduable
          KindOf = SELECTABLE VEHICLE
          VisionRange = 300.0
          ShroudClearingRange = 300.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
            SubdualDamageCap = 200
            SubdualDamageHealRate = 500
            SubdualDamageHealAmount = 50
          End
        End
        Object NotSubduable
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, Entity Unit) SubdualWorld(SimulationRate rate, string template)
    {
        var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Subdual.ini"] = SubdualIni });
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, template, owner: 1, x: 100, y: 100);
        world.Step();
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        return (world, feeder, fixture, world.Entities.Single(value => value.Template.Name == template));
    }

    private static DamageInfo Microwave(int amount) => new(default, Fixed.FromInt(amount), DamageTypes.SubdualVehicle, DeathTypes.Normal, false);

    public static void SubdualDamageDisablesAndWearsOff()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, _, fixture, unit) = SubdualWorld(rate, "Subduable");
            var body = (ActiveBody)unit.Body!;
            world.Damage(unit.Id, Microwave(60));
            Check.That(body.SubdualDamage == Fixed.FromInt(60) && !unit.Subdued, $"{rate}: 60 of 100 disables nothing yet.");
            Check.That(body.Health == body.MaxHealth, $"{rate}: subdual damage leaves the health alone.");
            world.Damage(unit.Id, Microwave(60));
            Check.That(unit.Subdued && (world.DisabledFlagsOf(unit) & DisabledMask.Subdued) != 0, $"{rate}: as much as it has health disables it.");
            world.Damage(unit.Id, Microwave(500));
            Check.That(body.SubdualDamage == Fixed.FromInt(200), $"{rate}: the cap holds ({body.SubdualDamage}).");

            // 200, and 50 less every half second from the last hit: 150, 100 (still subdued), 50 (free).
            var hit = world.Tick;
            var restored = world;
            var steps = new List<long>();
            var last = body.SubdualDamage;
            for (var step = 0; step < world.Frames(120) && last > Fixed.Zero; step++)
            {
                restored.Step();
                var now = ((ActiveBody)restored.FindEntity(unit.Id)!.Body!).SubdualDamage;
                if (now != last) steps.Add(restored.InFrames(restored.Tick - hit));
                last = now;
                if (steps.Count == 1 && now == Fixed.FromInt(150))
                {
                    var hash = restored.StateHash;
                    restored = WorldSnapshot.Restore(WorldSnapshot.Write(restored), fixture.Templates, fixture.Fingerprint);
                    Check.That(restored.StateHash == hash, $"{rate}: a save keeps it.");
                }
                var left = restored.FindEntity(unit.Id)!;
                Check.That(left.Subdued == (now >= Fixed.FromInt(100)), $"{rate}: disabled while it holds as much as it has health, {now}.");
            }
            Check.That(last == Fixed.Zero, $"{rate}: it wears off ({last}).");
            Check.That(steps.Count == 4 && steps[0] >= 14 && steps[0] <= 16 && steps[3] >= 59 && steps[3] <= 61, $"{rate}: a step every 15 frames ({string.Join(", ", steps)}).");
        }
    }

    public static void SubdualDamageNeedsABodyThatCanBeSubdued()
    {
        var (world, _, _, unit) = SubdualWorld(TestRate.Default, "NotSubduable");
        Check.That(world.Damage(unit.Id, Microwave(500)) == DamageOutcome.NoEffect && !unit.Subdued, "A body without a cap takes none.");
        Check.That(unit.Body!.Health == unit.Body.MaxHealth, "And nothing else.");
    }
}

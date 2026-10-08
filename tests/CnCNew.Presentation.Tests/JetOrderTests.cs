using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The attack order of a fighter (<see cref="StandingOrders"/>): it overflies its target, flies straight on for one weapon range, turns around and comes in for the next pass (a winged aircraft cannot stop to
/// turn, and it cannot fire inside its weapon's minimum range), and the attack ends when the airfield-reloaded ammunition is gone.
/// </summary>
internal static class JetOrderTests
{
    private static readonly PlayerId Me = new(1);
    private static readonly PlayerId Enemy = new(2);

    private static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/FXList.ini"] = "FXList FX_Test\nEnd\nFXList FX_Other\nEnd\n",
        ["Data/INI/Object/JetOrderFixture.ini"] = """
        Locomotor FighterLoco
          Surfaces = AIR
          Speed = 150.0
          Acceleration = 120.0
          Braking = 60.0
          MinSpeed = 60.0
          TurnRate = 120.0
          Lift = 120.0
          PreferredHeight = 60.0
          CloseEnoughDist = 4.0
          CirclingRadius = 100.0
          Appearance = WINGS
        End

        Weapon FighterGun
          PrimaryDamage = 1.0
          AttackRange = 320.0
          MinimumAttackRange = 100.0
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon FighterMissile
          PrimaryDamage = 1.0
          AttackRange = 320.0
          MinimumAttackRange = 100.0
          ClipSize = 2
          ClipReloadTime = 2000
          DelayBetweenShots = 100
          AutoReloadsClip = RETURN_TO_BASE
        End

        Object GunFighter
          Side = Alpha
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE
          VisionRange = 800.0
          ShroudClearingRange = 800.0
          Locomotor = SET_NORMAL FighterLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FighterGun
          End
          Behavior = JetAIUpdate Jet01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object MissileFighter
          Side = Alpha
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE
          VisionRange = 800.0
          ShroudClearingRange = 800.0
          Locomotor = SET_NORMAL FighterLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FighterMissile
          End
          Behavior = JetAIUpdate Jet01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SturdyTarget
          Side = Beta
          KindOf = SELECTABLE STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(JetOrderTests));
        harness.Run("A gun fighter on an attack order makes pass after pass: in, over, out for a weapon range, round and in again", GunFighterPasses);
        harness.Run("A missile fighter ends its attack when its airfield ammunition is gone", MissileFighterEndsItsAttack);
        harness.Run("Time scale: a standing attack order issues its commands on the original-frame grid and at the same game times at 30, 60 and 120 Hz", TimeScaleStandingOrders);
        return harness.Failures;
    }

    private sealed class Rig
    {
        private readonly InputTests.WorldSink _sink;
        private ulong _system;

        public Rig(SimulationRate? rate = null)
        {
            Fixture = new SyntheticFixture(rate ?? CnCNew.Tests.TestRate.Default, files: Files);
            var players = ImmutableArray.Create(new PlayerConfiguration(Me, "Me"), new PlayerConfiguration(Enemy, "Enemy"));
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), Fixture.Templates.Rate, 7, players, SyntheticFixture.FixtureId, Fixture.Fingerprint), Fixture.Templates);
            _sink = new InputTests.WorldSink(World, []);
        }

        public SyntheticFixture Fixture { get; }
        public SimulationWorld World { get; }
        public StandingOrders Orders { get; } = new();
        public List<SimCommand> Issued => _sink.Issued;

        public Entity Spawn(string template, PlayerId owner, int x, int y)
        {
            var command = new SpawnCommand(PlayerId.System, ++_system, World.Tick, template, owner, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero);
            TestHarness.Assert(World.Submit(command).Accepted, $"{template} spawns");
            World.Step();
            return World.Entities.Where(value => value.Template.Name == template).OrderByDescending(value => value.Id.Value).First();
        }

        public void Tick()
        {
            Orders.Update(World, Me, _sink.Issue);
            World.Step();
        }
    }

    private static float Distance(Entity a, Entity b) => (float)Math.Sqrt(Math.Pow((double)(a.X - b.X).ToDecimal(), 2) + Math.Pow((double)(a.Y - b.Y).ToDecimal(), 2));

    private static void GunFighterPasses()
    {
        var rig = new Rig();
        var target = rig.Spawn("SturdyTarget", Enemy, 1000, 1000);
        var jet = rig.Spawn("GunFighter", Me, 400, 1000);
        rig.Orders.Set(jet.Id, new StandingOrder(StandingOrderKind.Attack, target.Id, new Vector2(1000, 1000), [], rig.World.Tick));
        var passes = 0;
        var inside = false;
        var farthestAfterPass = new List<float>();
        var shots = 0;
        var slowest = Fixed.MaxValue;
        var shotDistances = new List<float>();
        for (var tick = 0; tick < CnCNew.Tests.TestTime.Frames(rig.World, 2400); tick++)
        {
            var before = rig.Issued.Count;
            rig.Tick();
            foreach (var command in rig.Issued.Skip(before).OfType<FireWeaponCommand>()) { shots++; shotDistances.Add(Distance(jet, target)); }
            var distance = Distance(jet, target);
            slowest = Fixed.Min(slowest, jet.Speed);
            if (distance < 100f && !inside) { inside = true; passes++; farthestAfterPass.Add(0f); }
            else if (distance >= 100f) inside = false;
            if (farthestAfterPass.Count > 0) farthestAfterPass[^1] = Math.Max(farthestAfterPass[^1], distance);
        }

        TestHarness.Assert(rig.World.RejectionCounts.Sum() == 0 || rig.World.RejectionCounts.Sum() < shots, "the simulation accepted the orders it was given");
        TestHarness.Assert(passes >= 3, $"at least three passes over the target in 80 seconds ({passes})");
        TestHarness.Assert(farthestAfterPass.Take(passes - 1).All(value => value >= 280f), $"after each overflight the jet flew out for about a weapon range before turning ({string.Join(", ", farthestAfterPass.Select(value => value.ToString("F0")))})");
        TestHarness.Assert(shots >= passes, $"the gun fired on the passes ({shots} shots)");
        TestHarness.Assert(shotDistances.All(value => value is >= 90f and <= 330f), $"every shot was inside the weapon's range band (measured after the tick's move) ({shotDistances.Min():F0}..{shotDistances.Max():F0})");
        TestHarness.Assert(slowest >= SimulationTimingMin, $"the fighter never flew slower than its minimum speed ({slowest})");
        Console.WriteLine($"       jet passes: {passes} passes, {shots} shots, farthest out {string.Join(", ", farthestAfterPass.Select(value => value.ToString("F0")))}");
        TestHarness.Assert(rig.Orders.TryGet(jet.Id, out _), "the attack order stands while the target lives");
    }

    /// <summary>
    /// Standing orders at 30, 60 and 120 Hz (docs/TICKRATE-MIGRATION.md §14.4): the orders are examined once per original frame, so a jet on an attack run is sent its
    /// commands on frame boundaries only, never twice in one frame, and the fire orders of a pass follow each other in the same game time (the weapon's delay; one
    /// original frame of tolerance). When the first order goes out depends on the jet's flight, which is the simulation's and is not judged here: it is printed.
    /// </summary>
    private static void TimeScaleStandingOrders()
    {
        var firstFire = new List<(SimulationRate Rate, double Seconds)>();
        var perSecond = new List<(SimulationRate Rate, double Count)>();
        var rhythm = new List<(SimulationRate Rate, double Seconds)>();
        var passOrders = new List<int>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var rig = new Rig(rate);
            var target = rig.Spawn("SturdyTarget", Enemy, 1000, 1000);
            var jet = rig.Spawn("GunFighter", Me, 400, 1000);
            // Give the order on a whole second, so every rate gives it at the same game time.
            while (rig.World.Tick % rig.World.Time.TicksPerSecond != 0) rig.Tick();
            var start = rig.World.Tick;
            rig.Orders.Set(jet.Id, new StandingOrder(StandingOrderKind.Attack, target.Id, new Vector2(1000, 1000), [], rig.World.Tick));
            const int seconds = 6;
            for (var tick = 0; tick < seconds * rig.World.Time.TicksPerSecond; tick++) rig.Tick();

            var issued = rig.Issued.Where(command => command.Tick >= start).ToArray();
            TestHarness.Assert(issued.Length > 0, $"at {rate} the order issued commands");
            TestHarness.Assert(issued.All(command => rig.World.Time.IsOriginalFrameBoundary(command.Tick)), $"at {rate} every command of the standing order is issued on a tick that begins an original frame");
            var fire = issued.OfType<FireWeaponCommand>().ToArray();
            TestHarness.Assert(fire.Length > 0, $"at {rate} the jet was ordered to fire");
            TestHarness.Assert(fire.GroupBy(command => rig.World.Time.ToOriginalFrames(command.Tick)).All(group => group.Count() == 1), $"at {rate} at most one fire order goes out per original frame");
            firstFire.Add((rate, (double)(fire[0].Tick - start) / rig.World.Time.TicksPerSecond));
            perSecond.Add((rate, (double)issued.Length / seconds));
            // The first pass: the gun's DelayBetweenShots of 300 ms sets the rhythm of the fire orders (the order goes out in the frame the weapon is ready again).
            var pass = fire.TakeWhile((command, index) => index == 0 || command.Tick - fire[index - 1].Tick < rig.World.Time.TicksPerSecond).ToArray();
            TestHarness.Assert(pass.Length >= 3, $"at {rate} the first pass has several fire orders ({pass.Length})");
            rhythm.Add((rate, (double)(pass[^1].Tick - pass[0].Tick) / (pass.Length - 1) / rig.World.Time.TicksPerSecond));
            passOrders.Add(pass.Length);
        }

        string Text(IEnumerable<(SimulationRate Rate, double Value)> values, string format) => string.Join(", ", values.Select(value => $"{value.Rate}: {value.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture)}"));
        Console.WriteLine($"       standing attack order: fire orders of the first pass every {Text(rhythm, "F4")} s ({string.Join(", ", passOrders)} orders); first fire order after {Text(firstFire, "F4")} s; commands per second {Text(perSecond, "F2")}");
        const double oneFrame = 1.0 / CnCNew.Data.OriginalFrame.PerSecond;
        foreach (var (rate, seconds) in rhythm)
            TestHarness.Assert(Math.Abs(seconds - rhythm[0].Seconds) <= oneFrame + 1e-9 && Math.Abs(seconds - 0.3) <= oneFrame + 1e-9, $"the fire orders of a pass are {seconds:F4} s apart at {rate} and {rhythm[0].Seconds:F4} s at {rhythm[0].Rate} (DelayBetweenShots 300 ms): more than one original frame apart");
        foreach (var (rate, count) in perSecond)
            TestHarness.Assert(count <= CnCNew.Data.OriginalFrame.PerSecond, $"at {rate} no more commands go out than original frames pass ({count:F2} per second)");
    }

    private static readonly Fixed SimulationTimingMin = CnCNew.Tests.TestRate.Default.Time.PerSecondToPerTick(60m) - Fixed.FromRaw(16);

    private static void MissileFighterEndsItsAttack()
    {
        var rig = new Rig();
        var target = rig.Spawn("SturdyTarget", Enemy, 1000, 1000);
        var jet = rig.Spawn("MissileFighter", Me, 400, 1000);
        var ai = jet.Modules.OfType<JetAIUpdate>().Single();
        rig.Orders.Set(jet.Id, new StandingOrder(StandingOrderKind.Attack, target.Id, new Vector2(1000, 1000), [], rig.World.Tick));
        var shots = 0;
        long emptiedAt = -1;
        long clearedAt = -1;
        var shotsAfterwards = 0;
        for (var tick = 0; tick < CnCNew.Tests.TestTime.Frames(rig.World, 1500); tick++)
        {
            var before = rig.Issued.Count;
            rig.Tick();
            var fired = rig.Issued.Skip(before).OfType<FireWeaponCommand>().Count();
            shots += fired;
            if (emptiedAt < 0 && ai.IsOutOfSpecialReloadAmmo(rig.World)) emptiedAt = rig.World.Tick;
            else if (emptiedAt >= 0) shotsAfterwards += fired;
            if (emptiedAt >= 0 && clearedAt < 0 && !rig.Orders.TryGet(jet.Id, out _)) clearedAt = rig.World.Tick;
        }

        TestHarness.Assert(emptiedAt > 0 && shots >= 2, $"the missiles were fired ({shots} shots, empty at {emptiedAt})");
        TestHarness.Assert(clearedAt > 0 && clearedAt - emptiedAt <= CnCNew.Tests.TestTime.Frames(rig.World, 3), $"the attack order ended with the last missile (empty at {emptiedAt}, order gone at {clearedAt})");
        TestHarness.Assert(shotsAfterwards == 0, "nothing was fired afterwards");
        TestHarness.Assert(!ai.IsOutOfSpecialReloadAmmo(rig.World) || ai.Phase is JetAIUpdate.JetPhase.ReturnToDeadAirfield or JetAIUpdate.JetPhase.CirclingDeadAirfield, $"the jet went home (phase {ai.Phase})");
    }
}

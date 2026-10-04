using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
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
        return harness.Failures;
    }

    private sealed class Rig
    {
        private readonly InputTests.WorldSink _sink;
        private ulong _system;

        public Rig()
        {
            Fixture = new SyntheticFixture(files: Files);
            var players = ImmutableArray.Create(new PlayerConfiguration(Me, "Me"), new PlayerConfiguration(Enemy, "Enemy"));
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), 7, players, SyntheticFixture.FixtureId, Fixture.Fingerprint), Fixture.Templates);
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
        for (var tick = 0; tick < 2400; tick++)
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

    private static readonly Fixed SimulationTimingMin = CnCNew.Simulation.Time.SimulationTiming.PerSecondToPerTick(60m) - Fixed.FromRaw(16);

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
        for (var tick = 0; tick < 1500; tick++)
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
        TestHarness.Assert(clearedAt > 0 && clearedAt - emptiedAt <= 3, $"the attack order ended with the last missile (empty at {emptiedAt}, order gone at {clearedAt})");
        TestHarness.Assert(shotsAfterwards == 0, "nothing was fired afterwards");
        TestHarness.Assert(!ai.IsOutOfSpecialReloadAmmo(rig.World) || ai.Phase is JetAIUpdate.JetPhase.ReturnToDeadAirfield or JetAIUpdate.JetPhase.CirclingDeadAirfield, $"the jet went home (phase {ai.Phase})");
    }
}

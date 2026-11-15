using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The nuclear missile shakes as it leaves its silo (<c>NeutronMissileUpdate::doAttack</c>, <c>SpecialJitterDistance</c>): the drawn missile stands off its logic position by an amount
/// that shrinks to nothing over <c>SpecialSpeedTime</c>, and stands at its position again from then on. The shake is the presentation's alone.
/// </summary>
internal static class MissileJitterTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha")];
    private const float Distance = 0.4f;
    private const int SpecialFrames = 45;

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/MissileJitterFixture.ini"] = """
        Object MjSilo
          KindOf = STRUCTURE
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = MjSiloModel
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
        End
        Object MjMissile
          KindOf = PROJECTILE
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = MjMissileModel
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 99999.0
            InitialHealth = 99999.0
          End
          Behavior = NeutronMissileUpdate Neutron01
            DistanceToTravelBeforeTurning = 5000.0
            MaxTurnRate = 7200.0
            ForwardDamping = 0.1
            RelativeSpeed = 2.0
            TargetFromDirectlyAbove = 300.0
            SpecialSpeedTime = 1500
            SpecialSpeedHeight = 160.0
            SpecialAccelFactor = 1.0
            SpecialJitterDistance = 0.4
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("MissileJitter");
        t.Run("the nuclear missile shakes after its launch, less and less, and stands still once SpecialSpeedTime is over, at 30, 60 and 120 Hz alike", Shakes);
        return t.Failures;
    }

    private static void Shakes()
    {
        var byRate = new Dictionary<int, Dictionary<long, Vector3>>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
            byRate[rate.TicksPerSecond] = Offsets(rate);
        var reference = byRate[30];
        foreach (var (hz, offsets) in byRate)
            foreach (var (frame, offset) in reference)
                Assert(offsets.TryGetValue(frame, out var other) && Vector3.Distance(offset, other) < 1e-4f,
                    $"At {hz} Hz the shake of frame {frame} after the launch is the one of 30 Hz ({other} against {offset}).");
    }

    /// <summary>The drawn offset from the logic position at the end of each logic frame after the launch, by that frame.</summary>
    private static Dictionary<long, Vector3> Offsets(SimulationRate rate)
    {
        var fixture = new SyntheticFixture(rate, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        world.Submit(new SpawnCommand(PlayerId.System, 1, world.Tick, "MjSilo", new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(100), Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, 2, world.Tick, "MjMissile", new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(100), Fixed.FromInt(10), Fixed.Zero));
        world.Step();
        var silo = world.Entities.Single(entity => entity.Template.Name == "MjSilo");
        var missile = world.Entities.Single(entity => entity.Template.Name == "MjMissile");
        var module = missile.Modules.OfType<NeutronMissileUpdate>().Single();
        module.Launch(world, new ProjectileLaunch(0, silo.Id, EntityId.Invalid, Fixed.FromInt(2000), Fixed.FromInt(100), Fixed.Zero, 0, Fixed.Zero, Fixed.Zero, Fixed.One));
        var projector = new FrameProjector(ObjectVisualCatalog.Build(fixture.Graph), new FrameProjectorOptions { SnapDistance = 1000f });
        var perFrame = world.Time.TicksPerOriginalFrame;
        var offsets = new Dictionary<long, Vector3>();
        long? launch = null;
        for (var step = 0; step < perFrame * (SpecialFrames + 30); step++)
        {
            world.Step();
            projector.Advance(world);
            launch ??= module.LaunchTick;
            if (launch is not { } at || (world.Tick - at) % perFrame != 0) continue;
            // The last tick of a frame: the drawable stands where the logic object is, plus the shake.
            var frame = (world.Tick - 1 - at) / perFrame;
            var drawn = projector.Project(1f).Drawables.Single(drawable => drawable.Entity == missile.Id);
            var logic = new Vector3(FixedConvert(missile.X), FixedConvert(missile.Y), FixedConvert(missile.Z));
            offsets[frame] = drawn.Position - logic;
        }
        Assert(launch is not null, "The missile was launched.");
        Assert(offsets[0] == Vector3.Zero, $"The frame of the launch has no shake yet ({offsets[0]}).");
        var early = 0f;
        var late = 0f;
        for (var frame = 1L; frame < SpecialFrames; frame++)
        {
            var amplitude = (1f - (float)frame / SpecialFrames) * Distance;
            var length = offsets[frame].Length();
            Assert(length <= amplitude * MathF.Sqrt(2f) + 1e-4f, $"Frame {frame}: the shake {length} stays within its amplitude {amplitude} ({rate.TicksPerSecond} Hz).");
            if (frame < SpecialFrames / 3) early = MathF.Max(early, length);
            if (frame >= SpecialFrames * 2 / 3) late = MathF.Max(late, length);
        }
        Assert(early > 0.1f, $"The missile shakes right after its launch (largest offset {early}, {rate.TicksPerSecond} Hz).");
        Assert(late < early && late < Distance / 3f * MathF.Sqrt(2f), $"The shake has shrunk toward the end of SpecialSpeedTime ({late} against {early}, {rate.TicksPerSecond} Hz).");
        for (var frame = (long)SpecialFrames; offsets.ContainsKey(frame); frame++)
            Assert(offsets[frame] == Vector3.Zero, $"Frame {frame}: after SpecialSpeedTime the missile is drawn at its position ({offsets[frame]}, {rate.TicksPerSecond} Hz).");
        Assert(offsets.ContainsKey(SpecialFrames + 10), "The missile was still flying ten frames after the shake.");
        return offsets;
    }

    private static float FixedConvert(Fixed value) => CnCNew.Presentation.FixedConvert.ToSingle(value);
}

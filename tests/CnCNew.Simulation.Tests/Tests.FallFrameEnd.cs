using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> FallFrameEndFiles() => new Dictionary<string, string>(LastBlockerFiles())
    {
        ["Data/INI/Object/FallFrameEnd.ini"] = """
        Object FeFallingBomb
          KindOf = PROJECTILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 75.0
          End
          Behavior = HeightDieUpdate Height01
            TargetHeight = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>
    /// F-156: an object let go in the air (the Daisy Cutter's bomb) falls its frame's step in shares, and the height die that looks at it in the first tick of a frame sees it where the
    /// frame ends, as it does at 30 Hz. Before, a free fall published no rest: the height die saw the momentary height, half a step higher at 60 Hz, and what the bomb's death makes
    /// (the gas that must fall on) began from that height - the gas fell for 29, 33 and 35 frames at 30, 60 and 120 Hz.
    /// </summary>
    public static void AFallingBombDiesAtTheHeightWhereItsFrameEnds()
    {
        (long Frame, long Z)? reference = null;
        foreach (var (rate, _) in WorldRates)
        {
            var fixture = new SyntheticFixture(rate, files: FallFrameEndFiles());
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var bomb = RateBombInTheAir(world, fixture, "FeFallingBomb", 400);
            bomb.Modules.OfType<PhysicsBehavior>().Single().StartFall(world, Fixed.FromInt(3), Fixed.Zero);
            (long Frame, long Z)? died = null;
            for (var step = 0; step < world.Frames(300) && died is null; step++)
            {
                world.Step();
                if (bomb.EffectivelyDead) died = (world.Time.ToOriginalFrames(world.Tick - 1), bomb.Z.Raw);
            }
            Check.That(died is not null, $"{rate}: the bomb dies on its way down.");
            reference ??= died;
            Check.That(died == reference, $"{rate}: the bomb dies in frame {died!.Value.Frame} at height {Fixed.FromRaw(died.Value.Z)}, as at 30 Hz (frame {reference!.Value.Frame}, height {Fixed.FromRaw(reference.Value.Z)}).");
        }
    }
}

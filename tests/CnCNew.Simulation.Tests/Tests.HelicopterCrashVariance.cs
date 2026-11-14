using System;
using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> CrashVarianceFiles() => new Dictionary<string, string>(TransportFiles())
    {
        ["Data/INI/Object/CrashVariance.ini"] = """
        Object Crasher
          KindOf = VEHICLE AIRCRAFT
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 50.0
          End
          Behavior = HelicopterSlowDeathBehavior Death01
            DestructionDelay = 99999999
            SpiralOrbitTurnRate = 140.0
            SpiralOrbitForwardSpeed = 350.0
            SpiralOrbitForwardSpeedDamping = .9999
            MinSelfSpin = 0.0
            MaxSelfSpin = 0.0
            SelfSpinUpdateDelay = 0
            SelfSpinUpdateAmount = 0.0
            FallHowFast = 5%
            MinBladeFlyOffDelay = 3000
            MaxBladeFlyOffDelay = 3000
            DelayFromGroundToFinalDeath = 500
          End
        End
        """,
    };

    /// <summary>Two crashers of the same place and heading, killed in consecutive frames; each one's displacement after <paramref name="frames"/> frames of its fall (the mirror of the other's if it spirals the other way).</summary>
    private static List<(decimal X, decimal Y)> CrashTracks(SimulationRate rate, RuleProfile rules, int crashes, int frames, int snapshotAfterFrames = -1)
    {
        var fixture = new SyntheticFixture(rate, files: CrashVarianceFiles(), rules: rules);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 9, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, string.Empty), fixture.Templates, null);
        var feeder = new Feeder(world);
        for (var index = 0; index < crashes; index++) feeder.Spawn(1, "Crasher", x: 5000, y: 5000 + index * 2000, z: 4000);
        world.Step();
        StepFrames(world, 3);
        var wrecks = world.Entities.Where(entity => entity.Template.Name == "Crasher").OrderBy(entity => entity.Id.Value).ToArray();
        var starts = wrecks.Select(wreck => (X: wreck.X.ToDecimal(), Y: wreck.Y.ToDecimal())).ToArray();
        foreach (var wreck in wrecks)
        {
            feeder.Kill(wreck.Id.Value);
            StepFrames(world, 1);
        }
        SimulationWorld? restored = null;
        for (var frame = 0; frame < frames; frame++)
        {
            StepFrames(world, 1);
            if (restored is not null)
            {
                StepFrames(restored, 1);
                Check.That(restored.StateHash == world.StateHash, $"{rate}: live and restored world agree {frame} frames on.");
            }
            if (frame == snapshotAfterFrames) restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, null);
        }
        return world.Entities.Where(entity => entity.Template.Name == "Crasher").OrderBy(entity => entity.Id.Value)
            .Select((wreck, index) => (wreck.X.ToDecimal() - starts[index].X, wreck.Y.ToDecimal() - starts[index].Y)).ToList();
    }

    /// <summary>Rule fix <c>helicopter-crash-variance</c> (issue 315, the owner's decision of 14 November 2026): the original profile plays one crash every time, the product's helicopters spiral left or right at rates of their own - the same at the same seed, at 30, 60 and 120 Hz, across a save.</summary>
    public static void HelicopterCrashesVaryUnderTheFix()
    {
        var fix = RuleProfile.Parse("helicopter-crash-variance");
        foreach (var rate in FlightRates)
        {
            var original = CrashTracks(rate, RuleProfile.Original, 2, 40);
            Check.That(original.Count == 2 && Math.Abs(original[0].X - original[1].X) < 2m && Math.Abs(original[0].Y - original[1].Y) < 2m, $"{rate}: in the original profile two crashes are the same (up to the one frame between the kills) ({original[0]} and {original[1]}).");
            var tracks = CrashTracks(rate, fix, 8, 40);
            Check.That(tracks.Count == 8, $"{rate}: eight crashes were tracked ({tracks.Count}).");
            Check.That(tracks.Distinct().Count() == 8, $"{rate}: no two of eight crashes are alike under the fix.");
            Check.That(tracks.Any(t => t.Y > 5m) && tracks.Any(t => t.Y < -5m),
                $"{rate}: crashes spiral to both sides ({string.Join("; ", tracks)}).");
            var again = CrashTracks(rate, fix, 8, 40);
            Check.That(again.SequenceEqual(tracks), $"{rate}: the same seed gives the same crashes.");
            var saved = CrashTracks(rate, fix, 8, 40, snapshotAfterFrames: 12);
            Check.That(saved.SequenceEqual(tracks), $"{rate}: a world restored in the middle of the crashes plays them as the live one.");
        }
    }
}

using CnCNew.Game.Data;
using CnCNew.Simulation.Time;

namespace CnCNew.Game.Tests;

/// <summary>
/// The game data of the tests that compare a recorded hash of something a world took part in (<c>baselines/screens.txt</c> with the score screen of a played match,
/// <c>baselines/hud-drawlist-1280x720.sha256</c>). Those hashes are defined at 30 Hz, so these tests name that rate whatever <c>--tick-rate</c> says; everything else
/// in the suite runs on the suite's own data. At 30 Hz this is the suite's data itself (nothing is loaded twice).
/// </summary>
internal static class GoldenRate
{
    private static GameData? _loaded;

    public static GameData Data(GameData suiteData, string contentDirectory)
        => suiteData.Rate == SimulationRate.Original ? suiteData : _loaded ??= GameData.Load(contentDirectory, rate: SimulationRate.Original);
}

using System.Diagnostics;
using CnCNew.Game.Live;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// Loads what a match can show while its load screen stands, as <c>GameClient::preloadAssets</c> does (<see cref="PreloadedModels"/> chooses it). A model
/// read when its object first appears stopped that frame for 15 to 40 ms (the owner's "micro lags", WORLD-06; <c>--frame-report</c> measures it), and building them a little in
/// every frame only moved the stops to the start of the match.
/// </summary>
internal static class ModelPreload
{
    private const double PumpIntervalMs = 100;

    public static void Run(ModelAssetCache assets, LiveGame match)
    {
        // A server drops a member it has not heard from for 10 s, and this takes that long on a busy machine; a match that already runs cannot wait for it at all.
        var remote = (match as LiveMatch)?.Remote;
        if (remote?.Client.Phase == CnCNew.Net.ClientPhase.InMatch) return;
        var start = Stopwatch.GetTimestamp();
        var pumped = start;
        var models = PreloadedModels.Of(match);
        var teams = match.Players.Select(player => match.Colors.Of(player.Id)).Distinct().ToArray();
        foreach (var model in models)
        {
            if (remote is not null && Stopwatch.GetElapsedTime(pumped).TotalMilliseconds >= PumpIntervalMs)
            {
                remote.Pump();
                pumped = Stopwatch.GetTimestamp();
            }

            if (assets.Get(model) is not { } asset) continue;
            foreach (var team in teams) asset.Prepare(team);
        }

        FrameReport.Preload(models.Count, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}

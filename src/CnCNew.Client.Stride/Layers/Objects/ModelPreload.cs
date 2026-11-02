using System.Diagnostics;
using CnCNew.Game.Live;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// Loads what a match can show while its load screen stands, as <c>GameClient::preloadAssets</c> does, for the sides that play and what belongs to nobody's side. A model
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
        var models = ModelsOf(match);
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

    /// <summary>A side fields what is of its own side and of the side it is a kind of: a general's side reads <c>AmericaAirForceGeneral</c>, the base <c>America</c>.</summary>
    private static IReadOnlyCollection<string> ModelsOf(LiveGame match)
    {
        var sides = match.Players.Select(player => match.World.FactionSideOf(player.Id)).Where(side => side.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var visual in match.Data.Visuals.Visuals)
        {
            if (!match.Data.Templates.TryGet(visual.TemplateName, out var template) || !Fielded(template.Side, visual.IsKindOf("PRELOAD"))) continue;
            foreach (var draw in visual.Draws)
                foreach (var state in draw.States)
                    if (state.Model is { Length: > 0 } model) names.Add(model);
        }

        return names;

        // PRELOAD names objects of every side in the data; a side that does not play never shows its own.
        bool Fielded(string side, bool preload) => (preload && side is "" or "Civilian")
            || sides.Any(playing => side.Length > 0 && (playing.StartsWith(side, StringComparison.OrdinalIgnoreCase) || side.StartsWith(playing, StringComparison.OrdinalIgnoreCase)));
    }
}

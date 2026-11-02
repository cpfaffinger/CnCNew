namespace CnCNew.Game.Live;

/// <summary>
/// What is read while the load screen stands, as <c>GameClient::preloadAssets</c> chooses it: what stands on the map, and what has kind <c>PRELOAD</c>. The original reads that for every side
/// and keeps the memory; this reads the armies of the sides that play, whole, because a unit built later would stop its frame.
/// </summary>
public static class PreloadedModels
{
    /// <summary>A mission has a civilian player, and its side holds every prop of the data: 5,000 models read for nothing, 17 s of a window that does not come.</summary>
    private const string NobodysArmy = "Civilian";

    public static IReadOnlyCollection<string> Of(LiveGame match)
    {
        var sides = match.Players.Select(player => match.World.FactionSideOf(player.Id)).Where(side => side.Length > 0 && !side.Equals(NobodysArmy, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var visual in match.Data.Visuals.Visuals)
            if (match.Data.Templates.TryGet(visual.TemplateName, out var template) && Fielded(template.Side, visual.IsKindOf("PRELOAD")))
                Add(visual);

        foreach (var entity in match.World.Entities)
            if (match.Data.Visuals.TryGet(entity.Template.Name, out var visual))
                Add(visual);

        return names;

        void Add(CnCNew.Presentation.Objects.ObjectVisual visual)
        {
            foreach (var draw in visual.Draws)
                foreach (var state in draw.States)
                    if (state.Model is { Length: > 0 } model) names.Add(model);
        }

        // PRELOAD names objects of every side in the data; a side that does not play never shows its own. A general's side reads AmericaAirForceGeneral, the base America.
        bool Fielded(string side, bool preload) => (preload && side is "" or NobodysArmy)
            || sides.Any(playing => side.Length > 0 && (playing.StartsWith(side, StringComparison.OrdinalIgnoreCase) || side.StartsWith(playing, StringComparison.OrdinalIgnoreCase)));
    }
}

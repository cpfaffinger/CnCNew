using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;

namespace CnCNew.Presentation.Objects;

/// <summary>A map object drawn as static scenery; <see cref="Selections"/> is its default-state choice per draw module.</summary>
public sealed record SceneryItem(int Index, ObjectVisual Visual, Vector3 Position, float Yaw, ImmutableArray<DrawModuleSelection> Selections);

/// <summary>What <see cref="StaticScenery.Build"/> skipped, counted by reason (and by template name where one exists), ordinal order.</summary>
public sealed record SceneryReport(
    int MapObjects,
    int Waypoints,
    int RoadOrBridgePoints,
    ImmutableSortedDictionary<string, int> UnknownTemplates,
    ImmutableSortedDictionary<string, int> WithoutModel);

public sealed record SceneryResult(ImmutableArray<SceneryItem> Items, SceneryReport Report);

/// <summary>
/// Turns the <c>ObjectsList</c> of a map into static drawables. Rule: every map object that is not a waypoint, not a road or bridge end point,
/// whose template exists and has at least one draw module that selects a model under the empty condition set becomes one
/// <see cref="SceneryItem"/> (position from the map, height from <c>groundHeight</c> when given, the map angle in radians, the template scale).
/// The simulation spawns some map objects itself (supply warehouses in <c>SkirmishMatchSession</c>); the <see cref="FrameProjector"/>
/// hides a scenery item for good as soon as a live entity of the same template appears within <see cref="FrameProjector.ClaimRadius"/>
/// of it. Scenery ignores the fog of war (like the terrain it stands on).
/// </summary>
public static class StaticScenery
{
    public static SceneryResult Build(ImmutableArray<MapObject> objects, ObjectVisualCatalog catalog, Func<float, float, float>? groundHeight = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var items = ImmutableArray.CreateBuilder<SceneryItem>();
        var unknown = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var withoutModel = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int waypoints = 0, roadPoints = 0;
        foreach (var mapObject in objects)
        {
            if (mapObject.IsWaypoint) { waypoints++; continue; }
            if (mapObject.IsRoadPoint || mapObject.IsBridgePoint) { roadPoints++; continue; }
            if (!catalog.TryGet(mapObject.Name, out var visual)) { unknown[mapObject.Name] = unknown.GetValueOrDefault(mapObject.Name) + 1; continue; }
            var selections = DefaultSelections(visual);
            if (selections.All(selection => selection.Model is null))
            {
                withoutModel[visual.TemplateName] = withoutModel.GetValueOrDefault(visual.TemplateName) + 1;
                continue;
            }
            var x = (float)mapObject.X;
            var y = (float)mapObject.Y;
            var z = groundHeight?.Invoke(x, y) ?? (float)mapObject.Z;
            items.Add(new SceneryItem(items.Count, visual, new Vector3(x, y, z), (float)mapObject.Angle, selections));
        }
        return new SceneryResult(items.ToImmutable(), new SceneryReport(objects.Length, waypoints, roadPoints,
            unknown.ToImmutableSortedDictionary(StringComparer.Ordinal), withoutModel.ToImmutableSortedDictionary(StringComparer.Ordinal)));
    }

    public static ImmutableArray<DrawModuleSelection> DefaultSelections(ObjectVisual visual)
        => [..visual.Draws.Select(draw => BestStateSelector.FindBest(draw, ModelConditionSet.Empty) is var index and >= 0
            ? new DrawModuleSelection(index, draw.States[index].Model)
            : new DrawModuleSelection(-1, null))];
}

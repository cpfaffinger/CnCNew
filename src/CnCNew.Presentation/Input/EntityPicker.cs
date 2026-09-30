using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

/// <summary>An entity as the cursor sees it: its projected geometry centre, its pick radius in pixels and its distance along the view ray.</summary>
public readonly record struct ScreenEntity(Entity Entity, Vector2 Center, float Radius, float Depth);

/// <summary>
/// Screen-space picking. The original casts a ray against the rendered geometry (<c>W3DView::pickDrawable</c>); this layer has no meshes, so it
/// approximates each object by its bounding circle: an object is under the cursor when the cursor lies within the projected geometry radius of
/// its projected centre (never less than <see cref="MinRadiusPixels"/>), and among several the one nearest the camera along the view ray wins,
/// then the smaller screen distance, then the lower id. A box selects objects whose projected centre lies inside it (<c>W3DView.cpp</c>
/// <c>iterateDrawablesInRegion</c>, the same rule as the original).
/// </summary>
public sealed class EntityPicker(float minRadiusPixels = 8f)
{
    private static readonly int UnselectableBit = ObjectStatusBits.Index("UNSELECTABLE");
    private static readonly int MaskedBit = ObjectStatusBits.Index("MASKED");

    public float MinRadiusPixels { get; } = minRadiusPixels;

    /// <summary>
    /// <c>Object::isSelectable</c> plus the <c>CanSelectDrawable</c> filters that exist here: <c>ALWAYS_SELECTABLE</c> or <c>SELECTABLE</c> without the
    /// <c>UNSELECTABLE</c>/<c>MASKED</c> status, alive, not being removed, not inside a container.
    /// </summary>
    public static bool IsSelectable(Entity entity)
    {
        if (entity.DestroyRequested || entity.ContainedBy.IsValid) return false;
        if (entity.Template.IsKindOf("ALWAYS_SELECTABLE")) return true;
        return entity.Template.IsKindOf("SELECTABLE") && !entity.EffectivelyDead && !entity.HasStatus(UnselectableBit) && !entity.HasStatus(MaskedBit);
    }

    /// <summary><c>Drawable::isMassSelectable</c>: selectable and not a structure (double-click, select matching and select all only take these).</summary>
    public static bool IsMassSelectable(Entity entity) => IsSelectable(entity) && !entity.Template.IsKindOf("STRUCTURE");

    /// <summary>Selectable and visible to the viewer (shrouded and undetected stealthed objects are not pickable).</summary>
    public static bool IsPickable(SimulationWorld world, PlayerId viewer, Entity entity) => IsSelectable(entity) && world.CanPlayerSee(viewer, entity);

    public static Vector3 CenterOf(Entity entity)
        => new((float)entity.X.ToDecimal(), (float)entity.Y.ToDecimal(), (float)(entity.Z + entity.Template.CenterZDelta).ToDecimal());

    public bool TryProject(IViewProjection view, Entity entity, out ScreenEntity projected)
    {
        projected = default;
        var center = CenterOf(entity);
        if (!view.TryWorldToScreen(center, out var screen)) return false;
        var radius = (float)entity.Template.BoundingCircleRadius.ToDecimal();
        var halfHeight = (float)(entity.Template.GeometryHeight.ToDecimal() / 2);
        var pixels = MinRadiusPixels;
        foreach (var offset in (ReadOnlySpan<Vector3>)[new(radius, 0, 0), new(0, radius, 0), new(0, 0, halfHeight)])
            if (offset != Vector3.Zero && view.TryWorldToScreen(center + offset, out var edge)) pixels = Math.Max(pixels, Vector2.Distance(screen, edge));
        var depth = view.TryGetRay(screen, out var origin, out var direction) ? Vector3.Dot(center - origin, direction) : 0f;
        projected = new ScreenEntity(entity, screen, pixels, depth);
        return true;
    }

    /// <summary>The pickable object under <paramref name="screen"/>, or null.</summary>
    public Entity? Pick(SimulationWorld world, PlayerId viewer, IViewProjection view, Vector2 screen)
    {
        ScreenEntity? best = null;
        foreach (var entity in world.Entities)
        {
            if (!IsPickable(world, viewer, entity) || !TryProject(view, entity, out var candidate)) continue;
            var distance = Vector2.Distance(candidate.Center, screen);
            if (distance > candidate.Radius) continue;
            if (best is not { } current || Better(candidate, distance, current, Vector2.Distance(current.Center, screen))) best = candidate;
        }
        return best?.Entity;
    }

    private static bool Better(ScreenEntity candidate, float distance, ScreenEntity current, float currentDistance)
    {
        if (candidate.Depth != current.Depth) return candidate.Depth < current.Depth;
        if (distance != currentDistance) return distance < currentDistance;
        return candidate.Entity.Id.Value < current.Entity.Id.Value;
    }

    /// <summary>Pickable objects whose projected centre lies in <paramref name="rect"/>, ascending by id.</summary>
    public ImmutableArray<Entity> InRect(SimulationWorld world, PlayerId viewer, IViewProjection view, ScreenRect rect)
    {
        var result = new List<Entity>();
        foreach (var entity in world.Entities)
            if (IsPickable(world, viewer, entity) && view.TryWorldToScreen(CenterOf(entity), out var screen) && rect.Contains(screen)) result.Add(entity);
        result.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        return [..result];
    }

    /// <summary>Objects on screen: the viewport rectangle.</summary>
    public ImmutableArray<Entity> OnScreen(SimulationWorld world, PlayerId viewer, IViewProjection view)
        => InRect(world, viewer, view, new ScreenRect(Vector2.Zero, view.ViewportSize));
}

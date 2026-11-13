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
/// Picking. The original casts a ray from the camera through the cursor against the rendered geometry and takes the first object it meets (<c>W3DView::pickDrawable</c>,
/// <c>RTS3DScene::castRay</c>); this layer has no meshes, so the ray is cast against each object's geometry volume - the box of a <c>BOX</c> turned by the object's angle, the upright
/// cylinder of a <c>CYLINDER</c>, the ball of a <c>SPHERE</c> - and the object whose volume it enters first wins. A small object is also picked within
/// <see cref="MinRadiusPixels"/> of its projected centre, at the depth of its centre. Among equal depths the smaller screen distance, then the lower id. A box selects objects whose
/// projected centre lies inside it (<c>W3DView.cpp</c> <c>iterateDrawablesInRegion</c>, the same rule as the original).
/// Until the owner's playtest of 3 November 2026 the depth was that of each object's centre, and the high centre of a building beat a dozer standing in front of its wall.
/// </summary>
public sealed class EntityPicker(float minRadiusPixels = 8f)
{
    private static readonly int UnselectableBit = ObjectStatusBits.Index("UNSELECTABLE");
    private static readonly int MaskedBit = ObjectStatusBits.Index("MASKED");

    public float MinRadiusPixels { get; } = minRadiusPixels;

    private readonly HashSet<uint> _scriptUnselectable = [];

    /// <summary>
    /// <c>Object::setSelectable</c> from a mission script (<c>UNIT_AFFECT_OBJECT_PANEL_FLAGS</c> "Selectable"): an object the script made unselectable is neither picked nor kept in a selection. A
    /// presentation switch; the simulation's own <c>UNSELECTABLE</c> status is untouched.
    /// </summary>
    public void SetScriptSelectable(EntityId entity, bool selectable)
    {
        if (selectable) _scriptUnselectable.Remove(entity.Value);
        else _scriptUnselectable.Add(entity.Value);
    }

    public bool IsScriptUnselectable(uint entity) => _scriptUnselectable.Contains(entity);

    public int ScriptUnselectableCount => _scriptUnselectable.Count;

    /// <summary>
    /// <c>Object::isSelectable</c> plus the <c>CanSelectDrawable</c> filters that exist here: <c>ALWAYS_SELECTABLE</c> or <c>SELECTABLE</c> without the
    /// <c>UNSELECTABLE</c>/<c>MASKED</c> status, alive, not being removed, not inside a container. A rider its container leaves in the world (a fire base's soldier, a portable
    /// structure) is drawn but no more selectable than an enclosed one: the original's selection skips every contained object (<c>SelectionXlat</c>: "we're contained"), and a
    /// portable structure is <c>CLICK_THROUGH</c> besides.
    /// </summary>
    public static bool IsSelectable(Entity entity)
    {
        if (entity.DestroyRequested || entity.ContainedBy.IsValid) return false;
        if (entity.Template.IsKindOf("ALWAYS_SELECTABLE")) return true;
        // Object::m_isSelectable starts as the template's KINDOF_SELECTABLE; a map's objectSelectable overrides it (updateObjValuesFromMapProperties).
        var own = (entity.ScriptDisableMask & Entity.ScriptUnselectable) != 0 ? false : (entity.ScriptDisableMask & Entity.ScriptSelectable) != 0 ? true : entity.Template.IsKindOf("SELECTABLE");
        return own && !entity.EffectivelyDead && !entity.HasStatus(UnselectableBit) && !entity.HasStatus(MaskedBit);
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
        view.GetScreenRay(screen, out var origin, out var direction);
        var depth = Vector3.Dot(center - origin, direction);
        projected = new ScreenEntity(entity, screen, pixels, depth);
        return true;
    }

    /// <summary>The pickable object under <paramref name="screen"/>, or null.</summary>
    public Entity? Pick(SimulationWorld world, PlayerId viewer, IViewProjection view, Vector2 screen)
    {
        view.GetScreenRay(screen, out var origin, out var direction);
        ScreenEntity? best = null;
        foreach (var entity in world.Entities)
        {
            if (!IsPickable(world, viewer, entity) || _scriptUnselectable.Contains(entity.Id.Value) || !TryProject(view, entity, out var candidate)) continue;
            var distance = Vector2.Distance(candidate.Center, screen);
            if (RayEnters(entity, origin, direction) is { } enter) candidate = candidate with { Depth = enter };
            else if (distance > MinRadiusPixels) continue;
            if (best is not { } current || Better(candidate, distance, current, Vector2.Distance(current.Center, screen))) best = candidate;
        }
        return best?.Entity;
    }

    /// <summary>Where the view ray enters the object's geometry volume (its distance along the ray), or null when it misses it.</summary>
    public static float? RayEnters(Entity entity, Vector3 origin, Vector3 direction)
    {
        var template = entity.Template;
        var major = (float)template.GeometryMajorRadius.ToDecimal();
        var minor = (float)template.GeometryMinorRadius.ToDecimal();
        var height = Math.Max((float)template.GeometryHeight.ToDecimal(), 1f);
        var center = CenterOf(entity);
        if (major <= 0f) return null;
        if (template.Geometry.Equals("SPHERE", StringComparison.OrdinalIgnoreCase))
        {
            var toCenter = origin - center;
            var b = Vector3.Dot(toCenter, direction);
            var c = Vector3.Dot(toCenter, toCenter) - major * major;
            var discriminant = b * b - c;
            if (discriminant < 0f) return null;
            var t = -b - MathF.Sqrt(discriminant);
            return t >= 0f ? t : (-b + MathF.Sqrt(discriminant) >= 0f ? 0f : null);
        }
        var bottom = center.Z - height / 2;
        // Into the object's own frame: its position at the origin, its heading along +x.
        var angle = (float)entity.Angle.ToDecimal();
        var (cos, sin) = (MathF.Cos(-angle), MathF.Sin(-angle));
        var relative = origin - new Vector3(center.X, center.Y, bottom);
        var local = new Vector3(relative.X * cos - relative.Y * sin, relative.X * sin + relative.Y * cos, relative.Z);
        var way = new Vector3(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos, direction.Z);
        float enter = 0f, leave = float.MaxValue;
        bool Slab(float start, float step, float low, float high)
        {
            if (MathF.Abs(step) < 1e-9f) return start >= low && start <= high;
            var (a, b) = ((low - start) / step, (high - start) / step);
            if (a > b) (a, b) = (b, a);
            enter = Math.Max(enter, a);
            leave = Math.Min(leave, b);
            return enter <= leave;
        }
        if (!Slab(local.Z, way.Z, 0f, height)) return null;
        if (template.IsBox) return Slab(local.X, way.X, -major, major) && Slab(local.Y, way.Y, -minor, minor) ? enter : null;
        // An upright cylinder: the part of the ray inside the circle, within the height.
        var qa = way.X * way.X + way.Y * way.Y;
        var qb = local.X * way.X + local.Y * way.Y;
        var qc = local.X * local.X + local.Y * local.Y - major * major;
        if (qa < 1e-12f) return qc <= 0f ? enter : null;
        var root = qb * qb - qa * qc;
        if (root < 0f) return null;
        var sqrt = MathF.Sqrt(root);
        var (inside, outside) = ((-qb - sqrt) / qa, (-qb + sqrt) / qa);
        enter = Math.Max(enter, inside);
        leave = Math.Min(leave, outside);
        return enter <= leave ? enter : null;
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
            if (IsPickable(world, viewer, entity) && !_scriptUnselectable.Contains(entity.Id.Value) && view.TryWorldToScreen(CenterOf(entity), out var screen) && rect.Contains(screen)) result.Add(entity);
        result.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        return [..result];
    }

    /// <summary>Objects on screen: the viewport rectangle.</summary>
    public ImmutableArray<Entity> OnScreen(SimulationWorld world, PlayerId viewer, IViewProjection view)
        => InRect(world, viewer, view, new ScreenRect(Vector2.Zero, view.ViewportSize));
}

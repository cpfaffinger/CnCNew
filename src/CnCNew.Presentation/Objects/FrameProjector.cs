using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

public sealed record FrameProjectorOptions
{
    /// <summary>Fog of war for this player (own, allied and currently visible objects, plus remembered structures); null shows everything.</summary>
    public PlayerId? Viewer { get; init; }

    public bool Night { get; init; }
    public bool Snow { get; init; }

    /// <summary>House colors; <see cref="TeamColors.Neutral"/> for everybody when null.</summary>
    public TeamColors? Colors { get; init; }

    public ImmutableArray<SceneryItem> Scenery { get; init; } = [];

    /// <summary>A move longer than this within one tick is a teleport and is not interpolated.</summary>
    public float SnapDistance { get; init; } = 100f;
}

/// <summary>
/// Projects the simulation into <see cref="RenderFrame"/>s. <see cref="Advance"/> is called once after every simulation tick and captures
/// everything a frame needs (transforms, conditions, selected states), so <see cref="Project"/> can run at any frame rate without touching
/// the world: it interpolates between the previous and the current capture with <c>alpha</c> in [0, 1]. Entities that did not exist in the
/// previous capture (or moved further than <see cref="FrameProjectorOptions.SnapDistance"/>) appear at their current transform; entities
/// the world no longer holds disappear with the capture that no longer contains them. Contained entities are not drawn.
/// With a viewer, enemy and neutral structures that were seen once stay as ghosts (their last seen capture, <see cref="DrawableInstance.IsGhost"/>)
/// until the viewer sees their position again (<c>Object::getShroudedStatus</c> / the reference's ghost objects). Static scenery (map objects the simulation
/// did not spawn: trees, props, civilian structures) follows the shroud too: the original draws every object through the shroud texture, where unexplored ground is
/// black, so scenery stays hidden until the viewer has explored the ground it stands on (exploring is permanent) and is drawn from then on, also in the fog.
/// </summary>
public sealed class FrameProjector
{
    /// <summary>A scenery item within this distance of a live entity of the same template is replaced by it for good.</summary>
    public const float ClaimRadius = 1f;

    /// <summary>How long a muzzle flash stays visible after a shot, in ticks (the original shows it for the first drawn frame of the recoil; at 30 ticks per second this is about 0.13 s).</summary>
    public const int MuzzleFlashTicks = 4;

    /// <summary><c>HEALING_ICON_DISPLAY_TIME</c> (<c>Drawable.cpp</c>): the wrench or cross over a healed object stays for three seconds after health last rose.</summary>
    public const int HealingIconTicks = 3 * 30;

    private readonly ObjectVisualCatalog _catalog;
    private readonly FrameProjectorOptions _options;
    private readonly ImmutableArray<ImmutableArray<DrawModuleSelection>> _scenerySelections;
    private readonly bool[] _claimed;
    private readonly bool[] _explored;
    private readonly TurretTracker _turrets = new();
    private readonly UnitConditionTracker _units = new();
    private readonly ImmutableDictionary<string, ImmutableArray<int>> _sceneryByTemplate;
    private readonly SortedDictionary<uint, Capture> _ghosts = [];
    private readonly HashSet<uint> _known = [];
    private Dictionary<uint, Capture> _previous = [];
    private Dictionary<uint, Capture> _current = [];
    private ImmutableArray<Capture> _currentOrdered = [];

    public FrameProjector(ObjectVisualCatalog catalog, FrameProjectorOptions? options = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = options ?? new FrameProjectorOptions();
        var context = BaseConditions(_options);
        _scenerySelections = [.._options.Scenery.Select(item => Select(item.Visual, context))];
        _claimed = new bool[_options.Scenery.Length];
        _explored = new bool[_options.Scenery.Length];
        _sceneryByTemplate = _options.Scenery.GroupBy(item => item.Visual.TemplateName, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(group => group.Key, group => group.Select(item => item.Index).ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The last simulated tick captured (-1 before the first <see cref="Advance"/>).</summary>
    public long Tick { get; private set; } = -1;

    /// <summary>Entities skipped because their template has no <see cref="ObjectVisual"/> in the catalog (last capture).</summary>
    public int MissingVisuals { get; private set; }

    public int ClaimedScenery => _claimed.Count(value => value);

    /// <summary>Feeds the events of the tick that was just simulated (the turrets follow the shots, <see cref="TurretTracker"/>); call before <see cref="Advance"/>.</summary>
    public void Observe(IEnumerable<SimEvent> events, SimulationWorld world)
    {
        foreach (var simEvent in events)
        {
            _turrets.Observe(simEvent, world);
            _units.Observe(simEvent, world);
        }
    }

    /// <summary><c>AIGroup::groupCheer</c> (the <c>ALL_CHEER</c> meta action): the objects show SPECIAL_CHEERING for <see cref="UnitConditionTracker.CheerTicks"/> ticks.</summary>
    public void Cheer(IEnumerable<EntityId> entities) => _units.Cheer(entities, Tick);

    public void Advance(SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        (_previous, _current) = (_current, _previous);
        _current.Clear();
        MissingVisuals = 0;
        Tick = world.Tick - 1;

        var entities = _options.Viewer is { } viewer ? world.EntitiesVisibleTo(viewer) : [..world.Entities.OrderBy(entity => entity.Id.Value)];
        var ordered = ImmutableArray.CreateBuilder<Capture>(entities.Count);
        foreach (var entity in entities)
        {
            if (entity.DestroyRequested || entity.ContainedBy.IsValid) continue;
            if (!_catalog.TryGet(entity.Template.Name, out var visual)) { MissingVisuals++; continue; }
            var previous = _previous.GetValueOrDefault(entity.Id.Value);
            var capture = CaptureEntity(entity, world, visual, previous);
            _current[entity.Id.Value] = capture;
            ordered.Add(capture);
            if (_known.Add(entity.Id.Value)) Claim(capture);
        }
        _currentOrdered = ordered.ToImmutable();
        if (world.Tick % 150 == 0)
        {
            _turrets.Prune(id => world.FindEntity(new EntityId(id)) is not null);
            _units.Prune(id => world.FindEntity(new EntityId(id)) is not null);
        }
        if (_options.Viewer is { } fogViewer)
        {
            UpdateGhosts(world, fogViewer);
            UpdateSceneryExploration(world, fogViewer);
        }
    }

    /// <summary>Scenery counts as explored once the ground under it was seen (permanent, so a revealed item is not tested again).</summary>
    private void UpdateSceneryExploration(SimulationWorld world, PlayerId viewer)
    {
        foreach (var item in _options.Scenery)
        {
            if (_explored[item.Index] || _claimed[item.Index]) continue;
            _explored[item.Index] = world.IsPositionExploredBy(viewer, Fixed.FromDecimal((decimal)item.Position.X), Fixed.FromDecimal((decimal)item.Position.Y));
        }
    }

    /// <summary>Scenery items hidden because the viewer has not explored their ground (0 without a viewer).</summary>
    public int ShroudedScenery => _options.Viewer is null ? 0 : _options.Scenery.Count(item => !_claimed[item.Index] && !_explored[item.Index]);

    public RenderFrame Project(float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        var drawables = ImmutableArray.CreateBuilder<DrawableInstance>(_currentOrdered.Length + _ghosts.Count + _options.Scenery.Length);
        var ghosts = _ghosts.Values.ToArray();
        var nextGhost = 0;
        foreach (var capture in _currentOrdered)
        {
            for (; nextGhost < ghosts.Length && ghosts[nextGhost].Id < capture.Id; nextGhost++)
                drawables.Add(ToDrawable(ghosts[nextGhost], ghosts[nextGhost].Position, ghosts[nextGhost].Yaw, true));
            var position = capture.Position;
            var yaw = capture.Yaw;
            if (_previous.TryGetValue(capture.Id, out var previous) && Vector3.Distance(previous.Position, capture.Position) <= _options.SnapDistance)
            {
                position = Vector3.Lerp(previous.Position, capture.Position, alpha);
                yaw = LerpAngle(previous.Yaw, capture.Yaw, alpha);
            }
            drawables.Add(ToDrawable(capture, position, yaw, false, _previous.GetValueOrDefault(capture.Id), alpha));
        }
        for (; nextGhost < ghosts.Length; nextGhost++)
            drawables.Add(ToDrawable(ghosts[nextGhost], ghosts[nextGhost].Position, ghosts[nextGhost].Yaw, true));
        var baseConditions = BaseConditions(_options);
        foreach (var item in _options.Scenery)
        {
            if (_claimed[item.Index] || (_options.Viewer is not null && !_explored[item.Index])) continue;
            drawables.Add(new DrawableInstance(EntityId.Invalid, item.Index, item.Visual, item.Position, item.Yaw, PlayerId.System, TeamColors.Neutral,
                1f, 1f, baseConditions, _scenerySelections[item.Index], null, null, true, false));
        }
        return new RenderFrame(Tick, alpha, _options.Viewer, drawables.ToImmutable());
    }

    /// <summary>Shortest-arc interpolation of two angles in radians.</summary>
    public static float LerpAngle(float from, float to, float alpha)
    {
        var delta = (to - from) % MathF.Tau;
        if (delta > MathF.PI) delta -= MathF.Tau;
        else if (delta < -MathF.PI) delta += MathF.Tau;
        return from + delta * alpha;
    }

    private Capture CaptureEntity(Entity entity, SimulationWorld world, ObjectVisual visual, Capture? previous)
    {
        var conditions = ConditionDeriver.Derive(entity, world, visual,
            new ConditionContext(_options.Night, _options.Snow, previous?.ConstructionWork));
        var health = entity.Body is { } body && body.MaxHealth > Fixed.Zero ? ToFloat(body.Health) / ToFloat(body.MaxHealth) : 1f;
        var construction = entity.HasStatus(ObjectStatusBits.UnderConstruction)
            ? Math.Min(1f, entity.ConstructionWork / (float)Math.Max(entity.Template.BuildTicks, 1))
            : 1f;
        // The distance rolled along the heading, signed (backwards is negative): a wheel rotates by TireRotationMultiplier times this (W3DTruckDraw accumulates multiplier * speed per frame).
        var position = new Vector3(ToFloat(entity.X), ToFloat(entity.Y), ToFloat(entity.Z));
        var travel = previous?.Travel ?? 0f;
        if (previous is not null)
        {
            var step = position - previous.Position;
            var distance = MathF.Sqrt(step.X * step.X + step.Y * step.Y);
            if (distance > 0f && distance <= _options.SnapDistance)
                travel += (step.X * MathF.Cos(previous.Yaw) + step.Y * MathF.Sin(previous.Yaw) >= 0f ? distance : -distance);
            travel %= 100000f;
        }

        // Drawable::drawHealing: the icon shows while the object is hurt and was healed (health rose by attemptHealing) within HealingIconTicks; construction progress is not healing.
        var healedAt = previous?.HealedAtTick ?? -1;
        if (previous is not null && !entity.HasStatus(ObjectStatusBits.UnderConstruction) && Math.Clamp(health, 0f, 1f) > previous.Health01 + 1e-4f) healedAt = world.Tick;
        var fired = 0;
        for (var slot = 0; slot < 3; slot++)
            if (entity.LastFireTickOf(slot) is >= 0 and var lastFire && world.Tick - lastFire <= MuzzleFlashTicks) fired |= 1 << slot;
        var main = _turrets.Update(entity, visual, 0, world.Tick, world);
        var alt = _turrets.Update(entity, visual, 1, world.Tick, world);
        conditions = conditions.Union(_units.Update(entity, world, world.Tick - 1, _turrets));
        return new Capture(entity.Id.Value, visual, entity.Owner, new Vector3(ToFloat(entity.X), ToFloat(entity.Y), ToFloat(entity.Z)),
            ToFloat(entity.Angle), entity.X, entity.Y, Math.Clamp(health, 0f, 1f), construction, conditions, Select(visual, conditions),
            entity.ConstructionWork, main?.Angle, main?.Pitch, alt?.Angle, alt?.Pitch, travel, fired, healedAt);
    }

    private void UpdateGhosts(SimulationWorld world, PlayerId viewer)
    {
        foreach (var capture in _currentOrdered)
            if (capture.Visual.IsStructure && capture.Owner != viewer && !world.AreAllies(viewer, capture.Owner))
                _ghosts.Remove(capture.Id);
        var remembered = new List<uint>();
        foreach (var (id, ghost) in _ghosts)
            if (_current.ContainsKey(id) || world.IsPositionVisibleTo(viewer, ghost.FixedX, ghost.FixedY)) remembered.Add(id);
        foreach (var id in remembered) _ghosts.Remove(id);
        foreach (var (id, previous) in _previous)
        {
            if (_current.ContainsKey(id) || !previous.Visual.IsStructure || previous.Owner == viewer || world.AreAllies(viewer, previous.Owner)) continue;
            if (!world.IsPositionVisibleTo(viewer, previous.FixedX, previous.FixedY)) _ghosts[id] = previous;
        }
    }

    private void Claim(Capture capture)
    {
        if (!_sceneryByTemplate.TryGetValue(capture.Visual.TemplateName, out var candidates)) return;
        foreach (var index in candidates)
        {
            var item = _options.Scenery[index];
            var dx = item.Position.X - capture.Position.X;
            var dy = item.Position.Y - capture.Position.Y;
            if (dx * dx + dy * dy <= ClaimRadius * ClaimRadius) _claimed[index] = true;
        }
    }

    private DrawableInstance ToDrawable(Capture capture, Vector3 position, float yaw, bool ghost, Capture? previous = null, float alpha = 1f) => new(
        new EntityId(capture.Id), -1, capture.Visual, position, yaw, capture.Owner, _options.Colors?.Of(capture.Owner) ?? TeamColors.Neutral,
        capture.Health01, capture.Construction01, capture.Conditions, capture.Selections,
        Blend(previous?.TurretYaw, capture.TurretYaw, alpha), Blend(previous?.TurretPitch, capture.TurretPitch, alpha), false, ghost,
        Blend(previous?.AltTurretYaw, capture.AltTurretYaw, alpha), Blend(previous?.AltTurretPitch, capture.AltTurretPitch, alpha), capture.Travel, capture.FiredSlots,
        capture.HealedAtTick >= 0 && Tick - capture.HealedAtTick <= HealingIconTicks && capture.Health01 < 1f && Tick > HealingIconTicks && !capture.Visual.IsKindOf("NO_HEAL_ICON") && !capture.Conditions.Contains(ModelConditions.Sold));

    private static float? Blend(float? from, float? to, float alpha) => to is null ? null : from is null ? to : LerpAngle(from.Value, to.Value, alpha);

    private static ImmutableArray<DrawModuleSelection> Select(ObjectVisual visual, ModelConditionSet conditions)
        => [..visual.Draws.Select(draw => BestStateSelector.FindBest(draw, conditions) is var index and >= 0
            ? new DrawModuleSelection(index, draw.States[index].Model)
            : new DrawModuleSelection(-1, null))];

    private static ModelConditionSet BaseConditions(FrameProjectorOptions options)
    {
        var set = ModelConditionSet.Empty;
        if (options.Night) set = set.With(ModelConditions.Night);
        if (options.Snow) set = set.With(ModelConditions.Snow);
        return set;
    }

    private static float ToFloat(Fixed value) => (float)(value.Raw / (double)Fixed.ScaleRaw);

    private sealed record Capture(
        uint Id, ObjectVisual Visual, PlayerId Owner, Vector3 Position, float Yaw, Fixed FixedX, Fixed FixedY, float Health01,
        float Construction01, ModelConditionSet Conditions, ImmutableArray<DrawModuleSelection> Selections, int ConstructionWork,
        float? TurretYaw = null, float? TurretPitch = null, float? AltTurretYaw = null, float? AltTurretPitch = null, float Travel = 0f, int FiredSlots = 0, long HealedAtTick = -1);
}

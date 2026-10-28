using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data;
using CnCNew.Data.Definitions;
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

    /// <summary>
    /// The game data of the selection flash; null projects no flash tint at all (<see cref="DrawableInstance.FlashTint"/> stays zero), for callers without the game's GameData.
    /// </summary>
    public SelectionFlashSettings? SelectionFlash { get; init; }

    /// <summary>A move longer than this within one tick is a teleport and is not interpolated.</summary>
    public float SnapDistance { get; init; } = 100f;

    /// <summary>
    /// Between two ticks the health bar, the construction progress, the travel of the wheels and treads and the pose of the chassis (pitch, roll, lift, steering,
    /// springs) are drawn where they are on their way, as position, heading and turret always were (docs/PRESENTATION-OBJECTS.md section 12). Off: they step with
    /// the tick, as before Phase 29 - for a comparison, and for whoever wants the steps. A frame drawn exactly at a tick is the same either way.
    /// </summary>
    public bool InterpolateValues { get; init; } = true;
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

    /// <summary>How long a muzzle flash stays visible after a shot, in original frames (the original shows it for the first drawn frame of the recoil; four frames are about 0.13 s).</summary>
    public const int MuzzleFlashFrames = 4;

    /// <summary><c>HEALING_ICON_DISPLAY_TIME</c> (<c>Drawable.cpp</c>, <c>LOGICFRAMES_PER_SECOND * 3</c>): the wrench or cross over a healed object stays for three seconds after health last rose, in original frames.</summary>
    public const int HealingIconFrames = 3 * OriginalFrame.PerSecond;

    /// <summary>The tracker tables are cleaned of entities that are gone every this many original frames (five seconds).</summary>
    public const int PruneIntervalFrames = 150;

    private readonly ObjectVisualCatalog _catalog;
    private readonly FrameProjectorOptions _options;
    private readonly ImmutableArray<ImmutableArray<DrawModuleSelection>> _scenerySelections;
    private readonly bool[] _claimed;
    private readonly bool[] _explored;
    private readonly bool[] _sceneryFogged;
    // Where each scenery item stands, as the simulation's vision queries want it. Scenery never moves, so the conversion is done once.
    private readonly Fixed[] _sceneryX;
    private readonly Fixed[] _sceneryY;
    private readonly TurretTracker _turrets = new();
    private readonly SuspensionTracker _suspension = new();
    private readonly UnitConditionTracker _units = new();
    private readonly StructureConditionTracker _structures = new();
    private readonly SelectionFlashTracker _flashes = new();
    private readonly ImmutableDictionary<string, ImmutableArray<int>> _sceneryByTemplate;
    private readonly SortedDictionary<uint, Capture> _ghosts = [];
    private readonly HashSet<uint> _known = [];
    private Dictionary<uint, Capture> _previous = [];
    private Dictionary<uint, Capture> _current = [];
    private ImmutableArray<Capture> _currentOrdered = [];
    private ImmutableArray<DrawableInstance> _guns = [];
    // The way each garrison gun last faced (GarrisonContain::trackTargets orients it while its rider has a victim), by GarrisonGunIndex.
    private readonly Dictionary<int, float> _gunYaw = [];

    /// <summary>The objects the last <see cref="Advance"/> captured, in the order of their ids: what the attached effects follow (<see cref="Fx.AttachedEffects"/>).</summary>
    public IEnumerable<Fx.ProjectedEntity> Projected
    {
        get
        {
            foreach (var capture in _currentOrdered) yield return new Fx.ProjectedEntity(new EntityId(capture.Id), capture.Visual, capture.Conditions, capture.Selections);
        }
    }
    // The durations above in ticks of the world last captured, and the original frame the projector's tick lies in (all three equal the frame numbers at 30 Hz).
    private long _healingIconTicks = HealingIconFrames;
    private long _frame = -1;

    public FrameProjector(ObjectVisualCatalog catalog, FrameProjectorOptions? options = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = options ?? new FrameProjectorOptions();
        var context = BaseConditions(_options);
        _scenerySelections = [.._options.Scenery.Select(item => Select(item.Visual, context))];
        _claimed = new bool[_options.Scenery.Length];
        _explored = new bool[_options.Scenery.Length];
        _sceneryFogged = new bool[_options.Scenery.Length];
        _sceneryX = new Fixed[_options.Scenery.Length];
        _sceneryY = new Fixed[_options.Scenery.Length];
        // Only a viewer asks the vision map about scenery.
        if (_options.Viewer is not null)
            foreach (var item in _options.Scenery)
            {
                _sceneryX[item.Index] = Fixed.FromDecimal((decimal)item.Position.X);
                _sceneryY[item.Index] = Fixed.FromDecimal((decimal)item.Position.Y);
            }
        _sceneryByTemplate = _options.Scenery.GroupBy(item => item.Visual.TemplateName, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(group => group.Key, group => group.Select(item => item.Index).ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A measuring tool's hook into <see cref="Advance"/> (told where its parts begin and end); null in the game.</summary>
    public IPresentationPhaseObserver? PhaseObserver { get; set; }

    /// <summary>The last simulated tick captured (-1 before the first <see cref="Advance"/>).</summary>
    public long Tick { get; private set; } = -1;

    /// <summary>Entities skipped because their template has no <see cref="ObjectVisual"/> in the catalog (last capture).</summary>
    public int MissingVisuals { get; private set; }

    public int ClaimedScenery => _claimed.Count(value => value);

    /// <summary>Feeds the events of the tick that was just simulated (deaths, crushes, ...; the turrets need none, the simulation owns them); call before <see cref="Advance"/>.</summary>
    public void Observe(IEnumerable<SimEvent> events, SimulationWorld world)
    {
        foreach (var simEvent in events)
        {
            _units.Observe(simEvent, world);
            if (_options.SelectionFlash is { } flash) _flashes.Observe(simEvent, world, _options.Colors, flash);
        }
    }

    /// <summary><c>AIGroup::groupCheer</c> (the <c>ALL_CHEER</c> meta action): the objects show SPECIAL_CHEERING for <see cref="UnitConditionTracker.CheerFrames"/> original frames.</summary>
    public void Cheer(IEnumerable<EntityId> entities, SimulationWorld world) => _units.Cheer(entities, Tick, world.Time.OriginalFrames(UnitConditionTracker.CheerFrames));

    private readonly Dictionary<uint, ColorValue> _houseColors = [];

    /// <summary>
    /// A mission script's colour flash (<c>doNamedFlash</c>): the drawable flashes <paramref name="color"/> (0..1 red, green, blue; <see langword="null"/> is the house colour of its owner) every half
    /// second for <paramref name="seconds"/> seconds. Presentation only. False when the entity is unknown or the flash settings are missing.
    /// </summary>
    public bool FlashScripted(EntityId entity, Vector3? color, int seconds, SimulationWorld world)
    {
        if (_options.SelectionFlash is null || world.FindEntity(entity) is not { } found) return false;
        var peak = color ?? HouseColorOf(found.Owner);
        _flashes.StartScripted(entity.Value, peak, seconds);
        return true;
    }

    private Vector3 HouseColorOf(PlayerId owner)
    {
        var house = _options.Colors?.Of(owner) ?? TeamColors.Neutral;
        return new Vector3(house.Red / 255f, house.Green / 255f, house.Blue / 255f);
    }

    /// <summary><c>Object::setCustomIndicatorColor</c> (<c>NAMED_CUSTOM_COLOR</c>): the house colour of one object, taken from the colour's alpha-red-green-blue integer; 0 gives the owner's colour back (<c>clearCustomIndicatorColor</c>).</summary>
    public bool SetHouseColor(EntityId entity, uint argb, SimulationWorld world)
    {
        if (world.FindEntity(entity) is null) return false;
        if (argb == 0) _houseColors.Remove(entity.Value);
        else _houseColors[entity.Value] = new ColorValue((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, 255);
        return true;
    }

    /// <summary>The scripted house colour of an entity, if a script gave it one.</summary>
    public ColorValue? HouseColorOverride(EntityId entity) => _houseColors.TryGetValue(entity.Value, out var color) ? color : null;

    public void Advance(SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        (_previous, _current) = (_current, _previous);
        _current.Clear();
        MissingVisuals = 0;
        Tick = world.Tick - 1;
        var time = world.Time;
        _healingIconTicks = time.OriginalFrames(HealingIconFrames);
        // The flash envelope is a table per original frame: it steps when the projector's tick begins one (every tick at 30 Hz).
        _frame = GameClock.OriginalFrameOf(Tick, time.TicksPerOriginalFrame);
        if (GameClock.BeginsOriginalFrame(Tick, time.TicksPerOriginalFrame)) _flashes.Advance(_frame);

        var observer = PhaseObserver;
        observer?.PhaseStarted(PresentationPhase.ProjectorAdvanceVisibleEntities);
        var entities = _options.Viewer is { } viewer ? world.EntitiesVisibleTo(viewer) : [..world.Entities.OrderBy(entity => entity.Id.Value)];
        observer?.PhaseEnded(PresentationPhase.ProjectorAdvanceVisibleEntities);
        observer?.PhaseStarted(PresentationPhase.ProjectorAdvanceCapture);
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
        _guns = GarrisonGuns(world);
        observer?.PhaseEnded(PresentationPhase.ProjectorAdvanceCapture);
        if (world.Tick % time.OriginalFrames(PruneIntervalFrames) == 0)
        {
            _turrets.Prune(id => world.FindEntity(new EntityId(id)) is not null);
            _suspension.Prune(id => world.FindEntity(new EntityId(id)) is not null);
            _units.Prune(id => world.FindEntity(new EntityId(id)) is not null);
            _structures.Prune(id => world.FindEntity(new EntityId(id)) is not null);
            _flashes.Prune(_frame, id => world.FindEntity(new EntityId(id)) is not null);
            foreach (var id in _houseColors.Keys.Where(id => world.FindEntity(new EntityId(id)) is null).ToArray()) _houseColors.Remove(id);
        }
        if (_options.Viewer is { } fogViewer)
        {
            observer?.PhaseStarted(PresentationPhase.ProjectorAdvanceGhosts);
            UpdateGhosts(world, fogViewer);
            observer?.PhaseEnded(PresentationPhase.ProjectorAdvanceGhosts);
            observer?.PhaseStarted(PresentationPhase.ProjectorAdvanceScenery);
            UpdateSceneryExploration(world, fogViewer);
            observer?.PhaseEnded(PresentationPhase.ProjectorAdvanceScenery);
        }
    }

    /// <summary>How often (original frames) the visibility of explored scenery is re-read; the fog changes slowly and the dimming has no transition.</summary>
    public const int SceneryFogIntervalFrames = 5;

    /// <summary>
    /// Scenery counts as explored once the ground under it was seen (permanent, so a revealed item is not tested again). Explored scenery that is not in sight right now is
    /// fogged (drawn dimmed); that flag is refreshed every <see cref="SceneryFogIntervalFrames"/> original frames.
    /// </summary>
    private void UpdateSceneryExploration(SimulationWorld world, PlayerId viewer)
    {
        var refreshFog = world.Tick % world.Time.OriginalFrames(SceneryFogIntervalFrames) == 0;
        foreach (var item in _options.Scenery)
        {
            if (_claimed[item.Index]) continue;
            var x = _sceneryX[item.Index];
            var y = _sceneryY[item.Index];
            var newlyExplored = false;
            if (!_explored[item.Index])
            {
                _explored[item.Index] = world.IsPositionExploredBy(viewer, x, y);
                newlyExplored = _explored[item.Index];
            }

            if (_explored[item.Index] && (refreshFog || newlyExplored)) _sceneryFogged[item.Index] = !world.IsPositionVisibleTo(viewer, x, y);
        }
    }

    /// <summary>Explored scenery items currently drawn dimmed because they are out of sight (0 without a viewer).</summary>
    public int FoggedScenery => _options.Viewer is null ? 0 : _options.Scenery.Count(item => !_claimed[item.Index] && _explored[item.Index] && _sceneryFogged[item.Index]);

    /// <summary>Scenery items hidden because the viewer has not explored their ground (0 without a viewer).</summary>
    public int ShroudedScenery => _options.Viewer is null ? 0 : _options.Scenery.Count(item => !_claimed[item.Index] && !_explored[item.Index]);

    /// <param name="ahead">
    /// 0 to 1: the next tick is late by this much of a tick (a match of a server whose bundle has not arrived). What moved in the last tick is drawn on along that
    /// step instead of standing still - an extrapolation of at most one tick. Nothing is remembered of it: the frame after the tick arrived is drawn from the tick's
    /// own state, so a unit that did not go on as assumed (it stopped, it turned) is simply drawn where it is.
    /// </param>
    public RenderFrame Project(float alpha, float ahead = 0f)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        ahead = alpha >= 1f ? Math.Clamp(ahead, 0f, 1f) : 0f;
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
                if (ahead > 0f) position += (capture.Position - previous.Position) * ahead;
            }
            drawables.Add(ToDrawable(capture, position, yaw, false, _previous.GetValueOrDefault(capture.Id), alpha));
        }
        for (; nextGhost < ghosts.Length; nextGhost++)
            drawables.Add(ToDrawable(ghosts[nextGhost], ghosts[nextGhost].Position, ghosts[nextGhost].Yaw, true));
        drawables.AddRange(_guns);
        var baseConditions = BaseConditions(_options);
        foreach (var item in _options.Scenery)
        {
            if (_claimed[item.Index] || (_options.Viewer is not null && !_explored[item.Index])) continue;
            drawables.Add(new DrawableInstance(EntityId.Invalid, item.Index, item.Visual, item.Position, item.Yaw, PlayerId.System, TeamColors.Neutral,
                1f, 1f, baseConditions, _scenerySelections[item.Index], null, null, true, false, Fogged: _options.Viewer is not null && _sceneryFogged[item.Index]));
        }
        return new RenderFrame(Tick, alpha, _options.Viewer, drawables.ToImmutable()) { Ahead = ahead };
    }

    /// <summary>The object the original draws at an occupied garrison point (<c>TheThingFactory->findTemplate("GarrisonGun")</c>).</summary>
    public const string GarrisonGunTemplate = "GarrisonGun";

    /// <summary><c>MUZZLE_FLASH_LIFETIME</c> (<c>LOGICFRAMES_PER_SECOND / 7</c>): the gun shows FIRING_A from the frame after its rider's shot until more than this many frames have passed.</summary>
    public const int GarrisonFlashFrames = OriginalFrame.PerSecond / 7;

    /// <summary>The key of a garrison gun among the drawables without an entity: negative, apart from scenery and the placement preview.</summary>
    public static int GarrisonGunIndex(EntityId building, int point) => -(1 << 20) - checked((int)(building.Value * 40 + (uint)point));

    /// <summary>
    /// <c>GarrisonContain::putObjectAtGarrisonPoint</c>, <c>trackTargets</c> and <c>updateEffects</c> (F-60, step 8): a gun barrel at every occupied point of an enclosing garrison
    /// the viewer sees, where its rider stands, facing the rider's victim (it keeps the way it faced while the rider has none), with FIRING_A in the frames after the rider's
    /// shot (not for a poison weapon). Drawn only - the gun is no object of the simulation.
    /// </summary>
    private ImmutableArray<DrawableInstance> GarrisonGuns(SimulationWorld world)
    {
        if (!_catalog.TryGet(GarrisonGunTemplate, out var gun)) return [];
        var guns = ImmutableArray.CreateBuilder<DrawableInstance>();
        var baseConditions = BaseConditions(_options);
        foreach (var capture in _currentOrdered)
        {
            if (world.FindEntity(new EntityId(capture.Id)) is not { Container: CnCNew.Simulation.Modules.BuiltIn.GarrisonContain { IsEnclosing: true } garrison } building) continue;
            foreach (var (point, riderId, target) in garrison.OccupiedPoints())
            {
                if (world.FindEntity(riderId) is not { } rider) continue;
                var key = GarrisonGunIndex(building.Id, point);
                var position = new Vector3(ToFloat(rider.X), ToFloat(rider.Y), ToFloat(rider.Z));
                var victim = world.FindEntity(world.AcquiredVictimOf(rider)) ?? world.FindEntity(target);
                if (victim is not null && (victim.X != rider.X || victim.Y != rider.Y))
                    _gunYaw[key] = MathF.Atan2(ToFloat(victim.Y) - position.Y, ToFloat(victim.X) - position.X);
                var conditions = GunFlashes(rider, world) ? baseConditions.Union(FiringA) : baseConditions;
                guns.Add(new DrawableInstance(EntityId.Invalid, key, gun, position, _gunYaw.GetValueOrDefault(key, capture.Yaw), building.Owner,
                    _options.Colors?.Of(building.Owner) ?? TeamColors.Neutral, 1f, 1f, conditions, Select(gun, conditions), null, null, false, false));
            }
        }
        foreach (var stale in _gunYaw.Keys.Where(key => !guns.Any(drawable => drawable.SceneryIndex == key)).ToArray()) _gunYaw.Remove(stale);
        return guns.ToImmutable();
    }

    private static readonly ModelConditionSet FiringA = ModelConditionSet.Parse("FIRING_A");
    private static readonly int PoisonDamage = NameTables.DamageType.TryIndexOf("POISON", out var poison) ? poison : throw new InvalidOperationException("POISON is not a damage type.");

    /// <summary>The rider fired, with a weapon that is not poison, between one and <see cref="GarrisonFlashFrames"/> + 1 original frames before the tick just simulated.</summary>
    private static bool GunFlashes(Entity rider, SimulationWorld world)
    {
        var last = -1L;
        var slotFired = -1;
        for (var slot = 0; slot < CnCNew.Simulation.Templates.WeaponSlot.Count; slot++)
            if (rider.LastFireTickOf(slot) is var tick && tick > last) (last, slotFired) = (tick, slot);
        if (last < 0) return false;
        var since = world.Tick - 1 - last;
        if (since < world.Time.OriginalFrames(1) || since >= world.Time.OriginalFrames(GarrisonFlashFrames + 2)) return false;
        return SimulationWorld.ActiveWeaponSet(rider) is not { } set || !set.HasWeapon(slotFired) || world.Templates.Weapons[set.Weapons[slotFired]].DamageType != PoisonDamage;
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
            new ConditionContext(_options.Night, _options.Snow, previous?.ConstructionWork, _options.Viewer));
        conditions = conditions.Union(_structures.Update(entity, world));
        var health = entity.Body is { } body && body.MaxHealth > Fixed.Zero ? ToFloat(body.Health) / ToFloat(body.MaxHealth) : 1f;
        var construction = entity.HasStatus(ObjectStatusBits.UnderConstruction)
            ? ToFloat(entity.ConstructionShare)
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

        // Drawable::drawHealing: the icon shows while the object is hurt and was healed (health rose by attemptHealing) within HealingIconFrames; construction progress is not healing.
        var healedAt = previous?.HealedAtTick ?? -1;
        if (previous is not null && !entity.HasStatus(ObjectStatusBits.UnderConstruction) && Math.Clamp(health, 0f, 1f) > previous.Health01 + 1e-4f) healedAt = world.Tick;
        var fired = 0;
        for (var slot = 0; slot < 3; slot++)
            if (entity.LastFireTickOf(slot) is >= 0 and var lastFire && world.Tick - lastFire <= world.Time.OriginalFrames(MuzzleFlashFrames)) fired |= 1 << slot;
        var main = _turrets.Update(entity, 0);
        var alt = _turrets.Update(entity, 1);
        var chassis = _suspension.Update(entity, visual, world, _options.SnapDistance) ?? ChassisPose.Level;
        conditions = conditions.Union(_units.Update(entity, world, world.Tick - 1, _turrets));
        // The selected states are a function of the visual and the condition bits alone; most objects keep both from one tick to the next.
        var selections = previous is not null && ReferenceEquals(previous.Visual, visual) && previous.Conditions == conditions ? previous.Selections : Select(visual, conditions);
        // GarrisonContain::getApparentControllingPlayer: a hidden stealth garrison is drawn in the colour of its owner from before for a non-ally, and remembered so in the fog (F-54).
        var owner = _options.Viewer is { } viewer && entity.Container?.ApparentOwnerFor(world, viewer) is { } apparent ? apparent : entity.Owner;
        return new Capture(entity.Id.Value, visual, owner, new Vector3(ToFloat(entity.X), ToFloat(entity.Y), ToFloat(entity.Z)),
            ToFloat(entity.Angle), entity.X, entity.Y, Math.Clamp(health, 0f, 1f), construction, conditions, selections,
            entity.ConstructionWork, main?.Angle, main?.Pitch, alt?.Angle, alt?.Pitch, travel, fired, healedAt, chassis);
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
        new EntityId(capture.Id), -1, capture.Visual, position, yaw, capture.Owner, _houseColors.TryGetValue(capture.Id, out var scripted) ? scripted : _options.Colors?.Of(capture.Owner) ?? TeamColors.Neutral,
        Mix(previous?.Health01, capture.Health01, alpha), Mix(previous?.Construction01, capture.Construction01, alpha), capture.Conditions, capture.Selections,
        Blend(previous?.TurretYaw, capture.TurretYaw, alpha), Blend(previous?.TurretPitch, capture.TurretPitch, alpha), false, ghost,
        Blend(previous?.AltTurretYaw, capture.AltTurretYaw, alpha), Blend(previous?.AltTurretPitch, capture.AltTurretPitch, alpha), Mix(previous?.Travel, capture.Travel, alpha), capture.FiredSlots,
        capture.HealedAtTick >= 0 && Tick - capture.HealedAtTick <= _healingIconTicks && capture.Health01 < 1f && Tick > _healingIconTicks && !capture.Visual.IsKindOf("NO_HEAL_ICON") && !capture.Conditions.Contains(ModelConditions.Sold),
        ghost, Mix(previous?.Chassis, capture.Chassis, alpha), ghost ? Vector3.Zero : _flashes.TintAt(capture.Id, _frame));

    private static float? Blend(float? from, float? to, float alpha) => to is null ? null : from is null ? to : LerpAngle(from.Value, to.Value, alpha);

    /// <summary>A value on its way from the last tick's to this tick's; this tick's when there was none before, at the tick itself, or when the option is off.</summary>
    private float Mix(float? from, float to, float alpha) => from is null || alpha >= 1f || !_options.InterpolateValues ? to : from.Value + (to - from.Value) * alpha;

    private ChassisPose Mix(ChassisPose? from, ChassisPose to, float alpha)
    {
        if (from is not { } before || alpha >= 1f || !_options.InterpolateValues) return to;
        float Part(float a, float b) => a + (b - a) * alpha;
        return new ChassisPose(Part(before.Pitch, to.Pitch), Part(before.Roll, to.Roll), Part(before.LiftZ, to.LiftZ), LerpAngle(before.WheelAngle, to.WheelAngle, alpha),
            Part(before.FrontLeft, to.FrontLeft), Part(before.FrontRight, to.FrontRight), Part(before.RearLeft, to.RearLeft), Part(before.RearRight, to.RearRight));
    }

    internal static ImmutableArray<DrawModuleSelection> Select(ObjectVisual visual, ModelConditionSet conditions)
    {
        var draws = visual.Draws;
        var selections = ImmutableArray.CreateBuilder<DrawModuleSelection>(draws.Length);
        foreach (var draw in draws)
        {
            var index = BestStateSelector.FindBest(draw, conditions);
            selections.Add(index >= 0 ? new DrawModuleSelection(index, draw.States[index].Model) : new DrawModuleSelection(-1, null));
        }
        return selections.MoveToImmutable();
    }

    private static ModelConditionSet BaseConditions(FrameProjectorOptions options)
    {
        var set = ModelConditionSet.Empty;
        if (options.Night) set = set.With(ModelConditions.Night);
        if (options.Snow) set = set.With(ModelConditions.Snow);
        return set;
    }

    private static float ToFloat(Fixed value) => FixedConvert.ToSingle(value);

    private sealed record Capture(
        uint Id, ObjectVisual Visual, PlayerId Owner, Vector3 Position, float Yaw, Fixed FixedX, Fixed FixedY, float Health01,
        float Construction01, ModelConditionSet Conditions, ImmutableArray<DrawModuleSelection> Selections, int ConstructionWork,
        float? TurretYaw = null, float? TurretPitch = null, float? AltTurretYaw = null, float? AltTurretPitch = null, float Travel = 0f, int FiredSlots = 0, long HealedAtTick = -1, ChassisPose Chassis = default);
}

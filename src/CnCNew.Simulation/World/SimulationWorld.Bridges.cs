using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

/// <summary>
/// The bridges of a map (<c>TerrainLogic</c>'s bridge list, <c>docs/SIMULATION-CORE.md</c> §67). What the map says is geometry and is made again whenever a world on that terrain is made; what
/// happens to a bridge is state, written only when the world has a bridge. A terrain bridge is a pair of bridge points with a <c>GenericBridge</c> object in its middle, a landmark bridge an
/// object of the map whose box is the deck and which has four towers. <c>W3DBridgeBuffer::loadBridges</c> runs before <c>GameLogic::startNewGame</c> places the landmarks, and both before
/// any other object, so that every world on a terrain starts with these objects.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary><c>Object::getLayer</c> of a world without bridges, and of everything on the ground.</summary>
    public const int GroundLayer = 0;

    // In the order they were made; TerrainLogic's list is newest first, and every question goes through it in that order.
    private readonly List<TerrainBridge> _bridges = [];
    private bool _bridgeStatesChanged;

    /// <summary>The bridges of the map, in the order they were made.</summary>
    public IReadOnlyList<TerrainBridge> Bridges => _bridges;

    /// <summary>
    /// <c>ActionManager::canRepairObject</c>: "here's the ensuring that they can't be repaired" - the original cut the repair of bridges and left everything behind the check in place. A test of what
    /// stands behind it lifts this for its world; the game never does. Not state.
    /// </summary>
    public bool BridgeRepairIsCut { get; set; } = true;

    /// <summary><c>TerrainLogic::anyBridgesDamageStatesChanged</c>: some bridge went into or out of a damage state since the frame began; the script engine reads it in the frame that follows.</summary>
    public bool BridgeStatesChanged => _bridgeStatesChanged;

    private void BuildBridges(bool placeObjects)
    {
        if (Terrain is not { } terrain || Navigation is not { } navigation) return;
        var generic = _templates.TryGet("GenericBridge", out var found) && found.IsInstantiable ? found : null;
        var renderIndex = 0;
        foreach (var span in terrain.BridgeSpans)
        {
            // W3DBridge::load: no block or no model of its name, no bridge.
            if (!_templates.Bridges.TryGet(span.Template, out var template) || !_templates.Geometry.BridgeDecks.TryGetValue(span.Template, out var deck)) continue;
            var from = new DeckPoint(span.FromX, span.FromY, terrain.HeightAt(span.FromX, span.FromY) + TerrainBridge.FloatAmount);
            var to = new DeckPoint(span.ToX, span.ToY, terrain.HeightAt(span.ToX, span.ToY) + TerrainBridge.FloatAmount);
            var bridge = TerrainBridge.FromSpan(span.Template, span.ScriptName, renderIndex++, from, to, deck.MinY, deck.MaxY, template.Scale);
            bridge.Layer = navigation.AddBridge(bridge);
            _bridges.Add(bridge);
            if (placeObjects && generic is not null) PlaceBridgeObject(bridge, generic);
        }
        foreach (var placed in terrain.BridgeObjects)
        {
            if (!_templates.TryGet(placed.Name, out var template) || !template.IsBridge || !template.IsInstantiable) continue;
            var (x, y) = (Fixed.FromDecimal(placed.X), Fixed.FromDecimal(placed.Y));
            var z = Fixed.FromDecimal(placed.Z) + terrain.HeightAt(x, y);
            var angle = FixedTrig.Normalize(Fixed.FromDecimal(placed.Angle));
            var bridge = TerrainBridge.FromObject(placed.Name, placed.Properties.GetText("objectName") ?? string.Empty, x, y, z, angle, template.GeometryMajorRadius, template.GeometryMinorRadius);
            bridge.Layer = navigation.AddBridge(bridge);
            _bridges.Add(bridge);
            if (!placeObjects) continue;
            var health = placed.Properties.TryGetInt("objectInitialHealth", out var value) ? value : 100;
            var id = RequestSpawn(template.Index, PlayerId.System, x, y, z, angle, health);
            MaterializePendingSpawns();
            var entity = FindEntity(id)!;
            entity.Z = z;
            bridge.ObjectId = id;
            if (entity.ModuleArray.OfType<BridgeBehavior>().FirstOrDefault() is { } behavior) CreateTowers(bridge, entity, behavior);
        }
    }

    private void PlaceBridgeObject(TerrainBridge bridge, ObjectTemplate generic)
    {
        var center = bridge.Center;
        var id = RequestSpawn(generic.Index, PlayerId.System, center.X, center.Y, center.Z, bridge.Angle);
        MaterializePendingSpawns();
        FindEntity(id)!.Z = center.Z;
        bridge.ObjectId = id;
    }

    /// <summary>The second half of <c>Bridge::Bridge(Object*)</c>: an object per corner, its major radius out from the corner across the deck, the 'from' towers looking back (<c>createTower</c>).</summary>
    private void CreateTowers(TerrainBridge bridge, Entity bridgeObject, BridgeBehavior behavior)
    {
        if (!_templates.Bridges.TryGet(bridge.TemplateName, out var template)) return;
        var (dx, dy) = (bridge.ToLeft.X - bridge.ToRight.X, bridge.ToLeft.Y - bridge.ToRight.Y);
        var length = Fixed.Sqrt(dx * dx + dy * dy);
        var (vx, vy) = length > Fixed.Zero ? (dx / length, dy / length) : (Fixed.Zero, Fixed.Zero);
        DeckPoint[] corners = [bridge.FromLeft, bridge.FromRight, bridge.ToLeft, bridge.ToRight];
        var offset = Fixed.FromInt(TerrainMap.CellSize) / Fixed.FromInt(2);
        for (var type = 0; type < TerrainBridge.MaxTowers; type++)
        {
            ObjectTemplate? tower = template.Towers[type].Length > 0 && _templates.TryGet(template.Towers[type], out var named) && named.IsInstantiable ? named : null;
            // The offset carries over from the tower before when one is missing, as in the original's loop.
            if (tower is not null) offset = tower.GeometryMajorRadius;
            var left = type is 0 or 2;
            var (x, y) = (corners[type].X + (left ? vx : -vx) * offset, corners[type].Y + (left ? vy : -vy) * offset);
            if (tower is null) continue;
            var angle = FixedTrig.Normalize(type < 2 ? bridgeObject.Angle + FixedTrig.Pi : bridgeObject.Angle);
            var id = RequestSpawn(tower.Index, bridgeObject.Owner, x, y, corners[type].Z, angle);
            MaterializePendingSpawns();
            var entity = FindEntity(id)!;
            entity.Z = corners[type].Z;
            behavior.SetTower(type, entity);
            if (entity.ModuleArray.OfType<BridgeTowerBehavior>().FirstOrDefault() is { } link)
            {
                link.SetBridge(bridgeObject);
                link.SetTowerType(type);
            }
            bridge.Towers[type] = id;
        }
    }

    private TerrainBridge? BridgeOnLayer(int layer)
    {
        if (layer == GroundLayer) return null;
        for (var index = _bridges.Count - 1; index >= 0; index--)
            if (_bridges[index].Layer == layer) return _bridges[index];
        return null;
    }

    /// <inheritdoc cref="IWorldServices.CreateNow"/>
    public Entity CreateNow(ObjectTemplate template, PlayerId owner, Fixed x, Fixed y, Fixed z, Fixed angle)
    {
        var entity = SpawnEntity(new PendingSpawn(Ids.AllocateEntity(), template.Index, owner, x, y, z, angle));
        entity.Z = z;
        return entity;
    }

    /// <inheritdoc cref="IWorldServices.SetBridgeUsable"/>
    public void SetBridgeUsable(TerrainBridge bridge, bool usable) => Navigation?.SetBridgeUsable(bridge.Layer, usable);

    /// <summary><c>Pathfinder::classifyObjectFootprint</c>: a bridge's object that stands more than a cell over the ground is no obstacle ("Don't add bounds that are up in the air").</summary>
    private bool BlocksGrid(Entity entity)
        => NavigationGrid.Blocks(entity.Template) && !(entity.Template.IsKindOf("BRIDGE") && entity.Z - GroundHeightAt(entity.X, entity.Y) > TerrainBridge.LayerCloseEnough);

    /// <summary><c>TerrainLogic::findBridgeAt</c>.</summary>
    public TerrainBridge? BridgeAt(Fixed x, Fixed y)
    {
        for (var index = _bridges.Count - 1; index >= 0; index--)
            if (_bridges[index].IsPointOnBridge(x, y)) return _bridges[index];
        return null;
    }

    /// <summary>The bridge a bridge object or a tower stands for.</summary>
    public TerrainBridge? BridgeOf(EntityId bridgeObject)
    {
        for (var index = _bridges.Count - 1; index >= 0; index--)
            if (_bridges[index].ObjectId == bridgeObject) return _bridges[index];
        return null;
    }

    /// <inheritdoc cref="IWorldServices.LayerHeightAt"/>
    public Fixed LayerHeightAt(Fixed x, Fixed y, int layer)
    {
        var height = GroundHeightAt(x, y);
        if (layer == GroundLayer || BridgeOnLayer(layer) is not { } bridge || !bridge.IsPointOnBridge(x, y)) return height;
        // "Don't return bridge height if it's in the ground."
        return Fixed.Max(bridge.HeightAt(x, y), height);
    }

    /// <summary>The height of the surface the entity stands on (<c>getLayerHeight(x, y, obj-&gt;getLayer())</c>).</summary>
    public Fixed SurfaceHeightUnder(Entity entity) => entity.Layer == GroundLayer ? GroundHeightAt(entity.X, entity.Y) : LayerHeightAt(entity.X, entity.Y, entity.Layer);

    /// <summary><c>TerrainLogic::getLayerForDestination</c>: the layer whose surface lies closest to the height.</summary>
    public int LayerForDestination(Fixed x, Fixed y, Fixed z)
    {
        if (_bridges.Count == 0) return GroundLayer;
        var best = GroundLayer;
        var distance = (z - GroundHeightAt(x, y)).Abs();
        for (var index = _bridges.Count - 1; index >= 0; index--)
        {
            var bridge = _bridges[index];
            if (bridge.Layer == GroundLayer || !bridge.IsPointOnBridge(x, y)) continue;
            var delta = (z - bridge.HeightAt(x, y)).Abs();
            if (delta < distance)
            {
                best = bridge.Layer;
                distance = delta;
            }
        }
        return best;
    }

    /// <summary>
    /// The layer of a place a ground unit is sent to. The original reads it from the height of the place (<c>getLayerForDestination</c>), which comes from the pick of the cursor, and a pick hits the
    /// deck before what lies under it (<c>W3DView::screenToTerrain</c>, <c>TerrainLogic::pickBridge</c>). An order of this port names no height: a place on a deck that stands is on that deck.
    /// </summary>
    public int LayerForGoal(Fixed x, Fixed y, LocomotorSurfaces surfaces)
    {
        if (_bridges.Count == 0 || (surfaces & LocomotorSurfaces.Ground) == 0) return GroundLayer;
        for (var index = _bridges.Count - 1; index >= 0; index--)
        {
            var bridge = _bridges[index];
            if (bridge.Layer != GroundLayer && bridge.DamageState != BodyDamageStates.Rubble && Navigation!.IsBridgeUsable(bridge.Layer) && bridge.IsPointOnBridge(x, y)) return bridge.Layer;
        }
        return GroundLayer;
    }

    /// <summary>
    /// The first deck a shell falling from <paramref name="zBefore"/> to <paramref name="zAfter"/> over (x, y) meets, and its height: where <c>getHighestLayerForDestination</c> turns from a layer to
    /// the ground for a projectile that is still over the deck (<c>DumbProjectileBehavior::update</c>, <c>MissileAIUpdate::detonate</c>: "blow'd up", two units above it for sloppy art). A broken deck stops it as well.
    /// </summary>
    public bool ProjectileHitsDeck(Fixed x, Fixed y, Fixed zBefore, Fixed zAfter, out Fixed deckZ)
    {
        deckZ = Fixed.Zero;
        for (var index = _bridges.Count - 1; index >= 0; index--)
        {
            var bridge = _bridges[index];
            if (!bridge.IsPointOnBridge(x, y)) continue;
            var height = bridge.HeightAt(x, y);
            if (zBefore < height || zAfter >= height) continue;
            deckZ = height + Fixed.FromInt(2);
            return true;
        }
        return false;
    }

    /// <summary>
    /// <c>TerrainLogic::objectInteractsWithBridgeLayer</c>: the object is on the bridge, or its bounds (minor radius plus half a cell) touch an end of it, its height is within
    /// <see cref="TerrainBridge.LayerCloseEnough"/> of the deck, and the bridge is not dead when that counts.
    /// </summary>
    public bool InteractsWithBridgeLayer(Entity entity, int layer, bool considerBridgeHealth = true)
    {
        if (BridgeOnLayer(layer) is not { } bridge) return false;
        var (x, y) = (entity.X, entity.Y);
        var radius = entity.Template.GeometryMinorRadius + Fixed.FromInt(TerrainMap.CellSize) / Fixed.FromInt(2);
        if (!bridge.IsPointOnBridge(x, y) && !bridge.IsCellOnEnd(x - radius, y - radius, x + radius, y + radius)) return false;
        if ((entity.Z - bridge.HeightAt(x, y)).Abs() > TerrainBridge.LayerCloseEnough) return false;
        return !considerBridgeHealth || bridge.DamageState != BodyDamageStates.Rubble;
    }

    /// <inheritdoc cref="IWorldServices.UpdateLayer"/>
    public void UpdateLayer(Entity entity, int layer)
    {
        if (layer != GroundLayer && !InteractsWithBridgeLayer(entity, layer)) layer = GroundLayer;
        SetLayer(entity, layer);
    }

    /// <inheritdoc cref="IWorldServices.SetLayer"/>
    public void SetLayer(Entity entity, int layer)
    {
        if (entity.Layer == layer) return;
        entity.Layer = layer;
        MarkDirty(entity);
    }

    /// <summary>
    /// <c>TerrainLogic::updateBridgeDamageStates</c> and <c>Bridge::updateDamageState</c>: a bridge that became rubble closes its layer and whatever is on it falls to its death ("just
    /// indiscriminately kill everything that was on the bridge"); one that came back from rubble opens it, unless scaffolding stands, which then has the say.
    /// </summary>
    public void UpdateBridgeDamageStates()
    {
        for (var index = _bridges.Count - 1; index >= 0; index--) UpdateBridgeDamageState(_bridges[index]);
        _bridgeStatesChanged = true;
    }

    private void UpdateBridgeDamageState(TerrainBridge bridge)
    {
        bridge.DamageStateChanged = false;
        if (!bridge.ObjectId.IsValid) return;
        if (FindEntity(bridge.ObjectId) is not { } bridgeObject)
        {
            bridge.ObjectId = EntityId.Invalid;
            return;
        }
        var state = bridgeObject.Body is ActiveBody body ? body.DamageState : BodyDamageStates.Pristine;
        var before = bridge.DamageState;
        if (state == before) return;
        bridge.DamageState = state;
        if (state == BodyDamageStates.Rubble)
        {
            Navigation?.SetBridgeUsable(bridge.Layer, false);
            bridge.DamageStateChanged = true;
            if (bridge.Layer != GroundLayer) KillWhatIsOnTheDeck(bridge);
        }
        if (before != BodyDamageStates.Rubble) return;
        if (bridgeObject.ModuleArray.OfType<BridgeBehavior>().FirstOrDefault() is not { IsScaffoldPresent: true }) Navigation?.SetBridgeUsable(bridge.Layer, true);
        bridge.DamageStateChanged = true;
    }

    private void KillWhatIsOnTheDeck(TerrainBridge bridge)
    {
        var fallen = new List<Entity>();
        foreach (var other in _entities.Values)
            if (other.Layer == bridge.Layer && !other.ContainedBy.IsValid && InteractsWithBridgeLayer(other, bridge.Layer, considerBridgeHealth: false)) fallen.Add(other);
        fallen.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        // DAMAGE_FALLING, DEATH_SPLATTED, from itself, HUGE_DAMAGE_AMOUNT.
        foreach (var entity in fallen) Damage(entity.Id, new DamageInfo(entity.Id, Fixed.FromInt(1_000_000_000), DamageTypes.Falling, DeathTypes.Splatted, false));
    }

    private void ForgetBridgeStateChange(long tick)
    {
        if (_bridgeStatesChanged && Time.IsOriginalFrameBoundary(tick)) _bridgeStatesChanged = false;
    }

    /// <summary>
    /// <c>Pathfinder::findBrokenBridge</c>: the broken bridge that would join the two places, which no way joins now, or nothing. The floods stand for the zones: a broken layer whose entry points
    /// touch what can be reached from each place joins them (<c>PathfindLayer::connectsZones</c>).
    /// </summary>
    public EntityId FindBrokenBridge(Fixed fromX, Fixed fromY, Fixed toX, Fixed toY, LocomotorSurfaces surfaces)
    {
        if (Navigation is not { } navigation || _bridges.All(bridge => bridge.DamageState != BodyDamageStates.Rubble)) return EntityId.Invalid;
        var near = navigation.ReachableCells(fromX, fromY, surfaces);
        if (navigation.Reaches(near, toX, toY)) return EntityId.Invalid;
        var layer = navigation.BrokenLayerJoining(near, navigation.ReachableCells(toX, toY, surfaces));
        return BridgeOnLayer(layer)?.ObjectId ?? EntityId.Invalid;
    }

    /// <summary>Who stands for a bridge in the state: its objects, what happened to it. Written only when the world has a bridge, so that every other world keeps its serialized form (and hash).</summary>
    private void DescribeBridges(IStateWriter writer)
    {
        if (_bridges.Count == 0) return;
        if (_bridgeStatesChanged) writer.Bool("bridgeStatesChanged", true);
        writer.BeginArray("bridges");
        foreach (var bridge in _bridges)
        {
            writer.BeginObject(null);
            writer.UInt("object", bridge.ObjectId.Value);
            writer.Int("damageState", bridge.DamageState);
            if (bridge.DamageStateChanged) writer.Bool("changed", true);
            if (Navigation is { } navigation && bridge.Layer != GroundLayer && !navigation.IsBridgeUsable(bridge.Layer)) writer.Bool("closed", true);
            for (var type = 0; type < TerrainBridge.MaxTowers; type++)
                if (bridge.Towers[type].IsValid) writer.UInt(writer.KeepsNames ? "tower" + type : "tower", bridge.Towers[type].Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadBridges(IStateReader state)
    {
        if (_bridges.Count == 0) return;
        _bridgeStatesChanged = state.Has("bridgeStatesChanged");
        var saved = state.Array("bridges");
        if (saved.Count != _bridges.Count) throw new InvalidDataException("The snapshot holds other bridges than the terrain.");
        for (var index = 0; index < saved.Count; index++)
        {
            var (item, bridge) = (saved[index], _bridges[index]);
            bridge.ObjectId = new EntityId(checked((uint)item.UInt("object")));
            bridge.DamageState = checked((int)item.Int("damageState"));
            bridge.DamageStateChanged = item.Has("changed");
            for (var type = 0; type < TerrainBridge.MaxTowers; type++)
                bridge.Towers[type] = item.Has("tower" + type) ? new EntityId(checked((uint)item.UInt("tower" + type))) : EntityId.Invalid;
            if (item.Has("closed")) Navigation?.SetBridgeUsable(bridge.Layer, false);
        }
    }
}

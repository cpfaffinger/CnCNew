using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// Repulsors and wandering (F-22): objects of <c>KINDOF_CAN_BE_REPULSED</c> - the civilians - run from what frightens them (<c>AI::findClosestRepulsor</c>), and afterwards wander around
/// where they stopped, for as long as nobody orders them otherwise: <c>AIMoveAwayFromRepulsorsState</c> and <c>AIWanderInPlaceState</c>, each the other's successor. A script sends any
/// object into the wandering (<c>doTeamWanderInPlace</c>); the path of <c>doTeamWander</c> and <c>doTeamPanic</c> stays with the script engine, which holds the paths, and ends in the
/// same running away. docs/SIMULATION-CORE.md section 68.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary><c>RepulsedDistance</c> (AIData.ini 150): how far beyond its vision a repulsed object runs. AIData.ini is not a typed definition yet, see the constants of the acquisition.</summary>
    private static readonly Fixed RepulsedDistance = Fixed.FromInt(150);

    /// <summary><c>IDLE_COUNTDOWN_DELAY</c> (AIStates.cpp): an idle object looks around this often, in logic frames of the original.</summary>
    private const int IdleLookPeriod = SimulationRate.OriginalFramesPerSecond * 2;

    /// <summary><c>Object::setStatus</c>: "only for a short amount of time" - the repulsor helper of a civilian takes the status back after two seconds, in logic frames of the original.</summary>
    private const int TemporaryRepulsorFrames = SimulationRate.OriginalFramesPerSecond * 2;

    /// <summary><c>MAX_SAFE_PATH_CELL_COUNT</c> (AIPathfind.cpp): "this is a rather expensive operation, so limit the search".</summary>
    private const int MaxSafePathCells = 2000;

    /// <summary>Ten logic frames of the original, the fixed part of <c>m_waitFrames</c>.</summary>
    private const int RoamLookBase = 10;

    private static readonly int RepulsorStatus = ObjectStatusBits.Index("REPULSOR");

    // Objects that can be repulsed, objects with the REPULSOR status and objects in a RoamState, by ascending id. Derived from the entities, not state of their own.
    private readonly SortedSet<uint> _repulsed = [];
    private readonly SortedSet<uint> _repulsors = [];
    private readonly SortedSet<uint> _roamers = [];
    private readonly List<uint> _repulsionScratch = [];

    private void RegisterRepulsion(Entity entity)
    {
        if (entity.Template.IsKindOf("CAN_BE_REPULSED") && AiOf(entity) is not null) _repulsed.Add(entity.Id.Value);
        if (entity.HasStatus(RepulsorStatus)) _repulsors.Add(entity.Id.Value);
        if (entity.Roam is not null) _roamers.Add(entity.Id.Value);
    }

    /// <summary>
    /// <c>Object::setStatus(OBJECT_STATUS_REPULSOR)</c>: a civilian repulses for two seconds, whoever set the status (<c>ObjectRepulsorHelper</c>), any other object until it is cleared.
    /// </summary>
    public void SetRepulsor(Entity entity, bool repulsor)
    {
        if (entity.HasStatus(RepulsorStatus) == repulsor) return;
        if (repulsor)
        {
            entity.Status |= 1UL << RepulsorStatus;
            _repulsors.Add(entity.Id.Value);
            if (entity.Template.IsKindOf("CAN_BE_REPULSED")) entity.RepulsorUntil = _tick + Time.OriginalFrames(TemporaryRepulsorFrames);
        }
        else
        {
            entity.Status &= ~(1UL << RepulsorStatus);
            _repulsors.Remove(entity.Id.Value);
            entity.RepulsorUntil = 0;
        }
        MarkDirty(entity);
    }

    /// <summary><c>ActiveBody::attemptDamage</c>: "Damaged repulsable civilians scare (repulse) other civs."</summary>
    public void NoteRepulsableDamage(Entity entity)
    {
        if (entity.Template.IsKindOf("CAN_BE_REPULSED")) SetRepulsor(entity, true);
    }

    /// <summary><c>AI::findClosestRepulsor</c> within the object's vision, measured between the bounding circles where the two frames end; ties go to the lower id. Null for none.</summary>
    public Entity? FindClosestRepulsor(Entity entity)
    {
        var search = new RepulsorSearch { Self = entity, Range = entity.Template.VisionRange, OwnRadius = CircleRadius(entity), BestDistance = Fixed.MaxValue };
        (search.X, search.Y, _) = entity.FrameEnd(_tick);
        foreach (var id in _repulsors)
            if (_entities.TryGetValue(id, out var flagged)) ConsiderRepulsor(flagged, ref search);
        if (RelationshipOverride is not null)
        {
            foreach (var candidate in _entities.Values) ConsiderRepulsor(candidate, ref search);
            return search.Best;
        }
        foreach (var player in Configuration.Players)
        {
            if (!AreEnemies(entity.Owner, player.Id)) continue;
            foreach (var candidate in EntitiesOwnedBy(player.Id).AsSpan()) ConsiderRepulsor(candidate, ref search);
        }
        return search.Best;
    }

    private struct RepulsorSearch
    {
        public Entity Self;
        public Fixed Range;
        public Fixed OwnRadius;
        public Fixed X;
        public Fixed Y;
        public Entity? Best;
        public Fixed BestDistance;
    }

    private void ConsiderRepulsor(Entity other, ref RepulsorSearch search)
    {
        if (other == search.Self || other.DestroyRequested || other.ContainedBy.IsValid) return;
        // "only stuff that isn't stealthed (and not detected)"
        if (other.HasStatus(StealthedStatus) && !other.HasStatus(DetectedStatus) && !other.HasStatus(DisguisedStatus)) return;
        if (!Repulses(search.Self, other)) return;
        var (x, y, _) = other.FrameEnd(_tick);
        var dx = x - search.X;
        var dy = y - search.Y;
        var distance = dx * dx + dy * dy;
        var radii = search.OwnRadius + CircleRadius(other);
        if (!SquaredReach.Of(search.Range + radii).Contains(distance)) return;
        var gap = Fixed.Max(Fixed.Zero, Fixed.Sqrt(distance) - radii);
        if (!IsNearer(other, gap, search.Best, search.BestDistance)) return;
        search.Best = other;
        search.BestDistance = gap;
    }

    /// <summary><c>PartitionFilterRepulsor::allow</c>: "If it's flagged, it's a repulsor"; otherwise a living enemy that can attack, with "buildings that can attack" among them and inert objects not.</summary>
    private bool Repulses(Entity self, Entity other)
    {
        if (other.HasStatus(RepulsorStatus)) return true;
        if (other.EffectivelyDead || !AreEnemies(self, other) || other.Template.IsKindOf("INERT")) return false;
        return MayAct(other, _tick) && ActiveWeaponSet(other) is { WeaponCount: > 0 };
    }

    /// <summary><c>privateWanderInPlace</c>: the object wanders around where it stands until an order replaces the state.</summary>
    public void BeginWanderInPlace(Entity entity)
    {
        if (AiOf(entity) is not { } ai || entity.Template.Locomotor is null) return;
        EndRoam(entity);
        ai.OrderIdle(this);
        EnterWanderInPlace(entity, ai, entity.Roam = new RoamState());
        _roamers.Add(entity.Id.Value);
        MarkDirty(entity);
    }

    /// <summary><c>AIIdleState::update</c>, and a wander or panic state that saw a repulsor: the state <c>AI_MOVE_AWAY_FROM_REPULSORS</c>.</summary>
    public void BeginMoveAwayFromRepulsors(Entity entity)
    {
        if (AiOf(entity) is not { } ai || entity.Template.Locomotor is null) return;
        EnterAway(entity, ai, entity.Roam ??= new RoamState());
        _roamers.Add(entity.Id.Value);
        MarkDirty(entity);
    }

    /// <summary>Any order replaces a roaming state and ends the panic (<c>AIPanicState::onExit</c>, <c>AIMoveAwayFromRepulsorsState::onExit</c>).</summary>
    private void EndRoam(Entity entity)
    {
        if (entity.Panicking)
        {
            entity.Panicking = false;
            MarkDirty(entity);
        }
        if (entity.Roam is null) return;
        entity.Roam = null;
        _roamers.Remove(entity.Id.Value);
        MarkDirty(entity);
    }

    /// <summary><c>AIPanicState::onEnter</c> and <c>onExit</c>: <c>MODELCONDITION_PANICKING</c>.</summary>
    public void SetPanicking(Entity entity, bool panicking)
    {
        if (entity.Panicking == panicking) return;
        entity.Panicking = panicking;
        MarkDirty(entity);
    }

    /// <summary>A move of a script's wander or panic: <c>AIInternalMoveToState</c>, which neither ends another state nor becomes an attack-move.</summary>
    public void RoamMove(Entity entity, Fixed x, Fixed y)
    {
        if (AiOf(entity) is not { } ai || !ai.StartInternalMove(x, y)) return;
        ai.BeginMoveWithNextFrame(this);
        WakeAt(entity, ai, Math.Min(ai.NextWake, _tick + _turretFrameTicks));
        MarkDirty(entity);
    }

    /// <summary>A place on the map for the goal of a wander: <c>computeGoal</c> keeps a goal off the outermost cell when its waypoint is on the map.</summary>
    public (Fixed X, Fixed Y) ClampedToMap(Fixed x, Fixed y)
    {
        if (Navigation is not { } navigation) return (x, y);
        var cell = Fixed.FromInt(TerrainMap.CellSize);
        var terrain = navigation.Terrain;
        return (Fixed.Min(Fixed.Max(x, cell), terrain.ExtentX - cell), Fixed.Min(Fixed.Max(y, cell), terrain.ExtentY - cell));
    }

    private void StepRepulsion(long tick)
    {
        if (_repulsors.Count == 0 && _repulsed.Count == 0 && _roamers.Count == 0) return;
        _repulsionScratch.Clear();
        _repulsionScratch.AddRange(_repulsors);
        foreach (var id in _repulsionScratch)
        {
            // ObjectRepulsorHelper::update
            if (!_entities.TryGetValue(id, out var repulsor)) _repulsors.Remove(id);
            else if (repulsor.RepulsorUntil != 0 && tick >= repulsor.RepulsorUntil) SetRepulsor(repulsor, false);
        }
        _repulsionScratch.Clear();
        _repulsionScratch.AddRange(_roamers);
        foreach (var id in _repulsionScratch)
        {
            if (!_entities.TryGetValue(id, out var roamer) || roamer.Roam is not { } state || AiOf(roamer) is not { } ai)
            {
                _roamers.Remove(id);
                continue;
            }
            if (!IsFirstTickOfFrame(roamer, tick) || !MayRoam(roamer, tick)) continue;
            StepRoam(roamer, ai, state);
        }
        _repulsionScratch.Clear();
        _repulsionScratch.AddRange(_repulsed);
        foreach (var id in _repulsionScratch)
        {
            if (!_entities.TryGetValue(id, out var civilian))
            {
                _repulsed.Remove(id);
                continue;
            }
            // AIIdleState::update sleeps IDLE_COUNTDOWN_DELAY between its looks, the first one shifted by a random part of it. Here the shift is the object's id, so that it takes no state.
            if (!IsFirstTickOfFrame(civilian, tick) || (Time.FramesIn(tick) + id) % IdleLookPeriod != 0) continue;
            if (civilian.Roam is not null || !MayRoam(civilian, tick) || civilian.ContainedBy.IsValid || AiOf(civilian) is not { IsIdle: true }) continue;
            if (FindClosestRepulsor(civilian) is not null) BeginMoveAwayFromRepulsors(civilian);
        }
    }

    private bool IsFirstTickOfFrame(Entity entity, long tick) => _firstFrameTicks == TickOrder.Next || (tick - entity.SpawnTick) % _firstFrameTicks == 0;

    private bool MayRoam(Entity entity, long tick)
        => !entity.EffectivelyDead && !entity.DestroyRequested && !entity.IsDisabledByTime(tick) && (entity.ScriptDisableMask & Entity.ScriptHeld) == 0;

    private void StepRoam(Entity entity, AIUpdateInterface ai, RoamState state)
    {
        if (state.Kind == RoamKind.AwayFromRepulsors)
        {
            if (!entity.HasMoveTarget) EnterWanderInPlace(entity, ai, state);
            return;
        }
        // AIWanderInPlaceState::update
        if (entity.Template.IsKindOf("CAN_BE_REPULSED"))
        {
            state.Timer--;
            if (state.Timer < 0)
            {
                state.Timer = state.WaitFrames;
                if (FindClosestRepulsor(entity) is not null)
                {
                    EnterAway(entity, ai, state);
                    MarkDirty(entity);
                    return;
                }
            }
            MarkDirty(entity);
        }
        if (!entity.HasMoveTarget) NextWanderGoal(entity, ai, state);
    }

    private void EnterWanderInPlace(Entity entity, AIUpdateInterface ai, RoamState state)
    {
        SetPanicking(entity, false);
        state.Kind = RoamKind.WanderInPlace;
        (state.OriginX, state.OriginY, _) = entity.FrameEnd(_tick);
        ai.ChooseLocomotorSet(this, "SET_WANDER");
        state.Timer = 0;
        state.WaitFrames = RoamLookBase + (int)(entity.Id.Value & 7);
        NextWanderGoal(entity, ai, state);
        MarkDirty(entity);
    }

    /// <summary>A goal around the origin: whole cells up to <c>WanderAboutPointRadius</c> along both axes.</summary>
    private void NextWanderGoal(Entity entity, AIUpdateInterface ai, RoamState state)
    {
        var cell = Fixed.FromInt(TerrainMap.CellSize);
        var delta = ai.CurrentLocomotor is { } locomotor ? (locomotor.WanderAboutPointRadius / cell + Fixed.FromDecimal(0.5m)).Floor() : 3;
        var x = state.OriginX + cell * Fixed.FromInt(LogicRandom.Range((int)-delta, (int)delta));
        var y = state.OriginY + cell * Fixed.FromInt(LogicRandom.Range((int)-delta, (int)delta));
        (x, y) = ClampedToMap(x, y);
        RoamMove(entity, x, y);
    }

    /// <summary><c>AIMoveAwayFromRepulsorsState::onEnter</c>: a state without a repulsor fails into the wandering, one with a repulsor runs on the panic locomotor.</summary>
    private void EnterAway(Entity entity, AIUpdateInterface ai, RoamState state)
    {
        if (FindClosestRepulsor(entity) is not { } enemy || Navigation is not { } navigation)
        {
            EnterWanderInPlace(entity, ai, state);
            return;
        }
        ai.ChooseLocomotorSet(this, "SET_PANIC");
        SetPanicking(entity, true);
        // AIUpdateInterface::requestSafePath: "save the prior repulsor".
        if (enemy.Id.Value != state.Repulsor1) (state.Repulsor2, state.Repulsor1) = (state.Repulsor1, enemy.Id.Value);
        state.Kind = RoamKind.AwayFromRepulsors;
        // doPathfind: a repulsor that is gone stands at (-1000, -1000), and the second one where the first stands.
        var (firstX, firstY) = RepulsorPlace(state.Repulsor1, (Fixed.FromInt(-1000), Fixed.FromInt(-1000)));
        var (secondX, secondY) = RepulsorPlace(state.Repulsor2, (firstX, firstY));
        var (ownX, ownY, _) = entity.FrameEnd(_tick);
        var surfaces = ai.CurrentLocomotor?.Surfaces ?? LocomotorSurfaces.None;
        var radius = entity.Template.VisionRange + RepulsedDistance;
        if (navigation.FindSafeGoal(ownX, ownY, surfaces, firstX, firstY, secondX, secondY, radius, MaxSafePathCells) is { } goal) RoamMove(entity, goal.X, goal.Y);
    }

    private (Fixed X, Fixed Y) RepulsorPlace(uint id, (Fixed X, Fixed Y) fallback)
    {
        if (id == 0 || !_entities.TryGetValue(id, out var repulsor)) return fallback;
        var (x, y, _) = repulsor.FrameEnd(_tick);
        return (x, y);
    }
}

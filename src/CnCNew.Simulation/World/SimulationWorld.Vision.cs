using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary>
    /// The shroud is recomputed every this many logic frames of the original (the reference refreshes on a partition cell timer; three frames keep the delay at 100 ms). An own rhythm
    /// of this simulation; the world steps it as <see cref="VisionInterval"/> ticks, after the last tick of every third logic frame (<see cref="_visionPhase"/>).
    /// </summary>
    public const int VisionIntervalFrames = 3;

    /// <summary>The shroud is recomputed once in this many ticks (<see cref="VisionIntervalFrames"/> in ticks of this world).</summary>
    public long VisionInterval { get; }

    /// <summary>
    /// The tick within <see cref="VisionInterval"/> after which the shroud is recomputed: the last tick of a logic frame of the original, when every object in phase with the world
    /// stands where the 30 Hz step of that frame puts it (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 5). Who reads the shroud inside a frame reads the state of the frame
    /// before, never one that 30 Hz does not have yet at that game time. 30 Hz: 0, the ticks that are a multiple of the interval, as ever.
    /// </summary>
    private readonly long _visionPhase;

    private static readonly int StealthedStatus = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedStatus = ObjectStatusBits.Index("DETECTED");

    private VisionMap? _vision;
    private Dictionary<int, int>? _playerSlots;

    // Work tables of RevealVision, rebuilt in every refresh (alliances change); kept only to avoid allocating them again. Not state.
    private int[][]? _visionViewers;
    private int[]? _visionViewerCounts;
    private int[]? _visionEverybody;

    /// <summary>The fog of war; <see langword="null"/> in a world without a terrain (everything is visible there).</summary>
    public VisionMap? Vision => _vision;

    /// <summary>The <see cref="VisionMap"/> slot of a player; the system player owns slot 0.</summary>
    public int VisionSlotOf(PlayerId player) => _playerSlots is not null && _playerSlots.TryGetValue(player.Value, out var slot) ? slot : throw new ArgumentException($"Player {player} has no vision slot.", nameof(player));

    private void InitializeVision()
    {
        var terrain = Terrain!;
        _playerSlots = [];
        for (var slot = 0; slot < _playerList.Length; slot++) _playerSlots[_playerList[slot].Id.Value] = slot;
        _vision = new VisionMap(terrain.CellsX, terrain.CellsY, _playerList.Length);
        _visionViewers = new int[_playerList.Length][];
        for (var slot = 0; slot < _playerList.Length; slot++) _visionViewers[slot] = new int[_playerList.Length];
        _visionViewerCounts = new int[_playerList.Length];
        _visionEverybody = [..Enumerable.Range(0, _playerList.Length).Where(slot => !_playerList[slot].Id.IsSystem)];
    }

    /// <summary>
    /// Recomputes the visible cells: <c>Object::look</c> — every living, uncontained object with a positive shroud clearing range reveals it to
    /// its owner and the owner's allies (<c>KINDOF_REVEAL_TO_ALL</c> to everybody); a structure under construction reveals its bounding circle. A look stands where it was taken until
    /// the object enters another partition cell or its sight or its viewers change, and then goes on for <c>UnlookPersistDuration</c> (<c>queueUndoShroudReveal</c>): the fog closes behind
    /// a unit five seconds late, and a dead scout's last look stays that long.
    /// </summary>
    private void RefreshVision()
    {
        RevealVision();
        HashVision();
    }

    /// <summary>
    /// The hash over the vision cells as they are now (<see cref="VisionMap.Hash"/>). The map keeps the value and only works when a cell byte
    /// changed since the last call, so the callers that change cells (refresh, script reveals, a load) need not recompute it themselves.
    /// </summary>
    private ulong HashVision() => _vision!.CurrentHash();

    /// <summary>The reveal part of <see cref="RefreshVision"/> without the hash over the cells (the two are separate tick phases for a measuring tool).</summary>
    private void RevealVision()
    {
        var vision = _vision!;
        vision.BeginCollectedRefresh();
        var allySlots = _visionViewers!;
        var allyCounts = _visionViewerCounts!;
        for (var slot = 0; slot < _playerList.Length; slot++)
        {
            var count = 0;
            for (var other = 0; other < _playerList.Length; other++)
                if (!_playerList[other].Id.IsSystem && (other == slot || AreAllies(_playerList[slot].Id, _playerList[other].Id))) allySlots[slot][count++] = other;
            allyCounts[slot] = count;
        }
        var anySightBonus = false;
        foreach (var player in _players.Values) anySightBonus |= player.BattlePlan is { SearchAndDestroy: > 0 };
        foreach (var entity in _entities.Values)
        {
            if (entity.Owner.IsSystem || entity.DestroyRequested || entity.EffectivelyDead || entity.ContainedBy.IsValid)
            {
                Unlook(entity);
                continue;
            }
            var range = entity.HasStatus(ObjectStatusBits.UnderConstruction) ? entity.Template.BoundingCircleRadius : entity.Template.ShroudClearingRange;
            if (TryShroudOverride(entity, out var overridden)) range = overridden;
            if (entity.ShroudRange > Fixed.Zero) range = entity.ShroudRange;
            if (range <= Fixed.Zero)
            {
                Unlook(entity);
                continue;
            }
            // The strategy center's own wider shroud and the search-and-destroy plan's sight bonus for the army.
            if (entity.ShroudScalar != Fixed.One) range *= entity.ShroudScalar;
            if (anySightBonus) range *= BattlePlanSightScalarOf(entity);
            var ownerSlot = _playerSlots![entity.Owner.Value];
            var viewers = entity.Template.IsKindOf("REVEAL_TO_ALL") ? _visionEverybody.AsSpan() : allySlots[ownerSlot].AsSpan(0, allyCounts[ownerSlot]);
            var mask = 0UL;
            foreach (var slot in viewers) mask |= 1UL << slot;
            // SpyVisionUpdate: a player that spies through this object (reference count above zero) sees what it sees, though not its allies.
            if (entity.VisionSpiedBy is { Count: > 0 } spied)
                for (var index = 0; index < spied.Count; index++)
                    if (spied.Values[index] > 0 && SlotOf(new PlayerId(spied.Keys[index])) is var spy and >= 0) mask |= 1UL << spy;
            var (cellX, cellY) = (LookCell(entity.X), LookCell(entity.Y));
            if (entity.Look is not { } look || look.CellX != cellX || look.CellY != cellY || look.Range != range || look.Viewers != mask)
            {
                Unlook(entity);
                entity.Look = look = new LookRecord(entity.X, entity.Y, range, mask, cellX, cellY);
            }
            RevealTo(vision, look.Viewers, look.X, look.Y, look.Range);
            if (entity.Template.ShroudRevealToAllRange > Fixed.Zero) RevealToEnemies(vision, entity, ownerSlot);
        }
        var now = Time.FramesIn(_tick);
        while (_unlooks.Count > 0 && _unlooks.Peek().Until < now) _unlooks.Dequeue();
        foreach (var (_, look) in _unlooks) RevealTo(vision, look.Viewers, look.X, look.Y, look.Range);
        vision.EndCollectedRefresh();
    }

    /// <summary>
    /// <c>Object::look</c>: "reveal to everyone if we're special", at a range of its own, to enemies and neutrals, not while it is under construction, and "only when the unit is visible" (the stealth
    /// general's Scud Storm).
    /// </summary>
    private void RevealToEnemies(VisionMap vision, Entity entity, int ownerSlot)
    {
        if (entity.HasStatus(ObjectStatusBits.UnderConstruction)) return;
        if (entity.HasStatus(StealthedBit) && !entity.HasStatus(DetectedBit) && !entity.HasStatus(DisguisedBit)) return;
        for (var slot = 0; slot < _playerList.Length; slot++)
            if (!_playerList[slot].Id.IsSystem && slot != ownerSlot && !AreAllies(_playerList[ownerSlot].Id, _playerList[slot].Id)) vision.Reveal(slot, entity.X, entity.Y, entity.Template.ShroudRevealToAllRange);
    }

    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedBit = ObjectStatusBits.Index("DETECTED");
    private static readonly int DisguisedBit = ObjectStatusBits.Index("DISGUISED");

    /// <inheritdoc cref="IWorldServices.RevealOnce"/>
    public void RevealOnce(PlayerId player, Fixed x, Fixed y, Fixed range)
    {
        if (_vision is null || SlotOf(player) is not (>= 0 and var slot)) return;
        _unlooks.Enqueue((Time.FramesIn(_tick), new LookRecord(x, y, range, 1UL << slot, 0, 0)));
    }

    private void RevealTo(VisionMap vision, ulong viewers, Fixed x, Fixed y, Fixed range)
    {
        for (var slot = 0; viewers != 0; slot++, viewers >>= 1)
            if ((viewers & 1) != 0) vision.Reveal(slot, x, y, range);
    }

    private int LookCell(Fixed coordinate) => checked((int)(coordinate / _templates.LookCellSize).Floor());

    /// <summary><c>m_pendingUndoShroudReveals</c>: the looks that were given up, oldest first, with the logic frame they go in.</summary>
    private readonly Queue<(long Until, LookRecord Look)> _unlooks = [];

    /// <summary><c>Object::unlook</c>: the object stops looking from where it looked, and that look goes on for <c>UnlookPersistDuration</c>.</summary>
    private void Unlook(Entity entity)
    {
        if (_vision is null || entity.Look is not { } look) return;
        entity.Look = null;
        _unlooks.Enqueue((Time.FramesIn(_tick) + _templates.UnlookPersistFrames, look));
    }

    private void DescribeLooks(IStateWriter writer)
    {
        if (_vision is null) return;
        writer.BeginArray("looks");
        foreach (var entity in _entities.Values)
        {
            if (entity.Look is not { } look) continue;
            writer.BeginObject(null);
            writer.UInt("entity", entity.Id.Value);
            DescribeLook(writer, look);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("unlooks");
        foreach (var (until, look) in _unlooks)
        {
            writer.BeginObject(null);
            writer.Int("until", until);
            DescribeLook(writer, look);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private static void DescribeLook(IStateWriter writer, LookRecord look)
    {
        writer.Fixed("x", look.X);
        writer.Fixed("y", look.Y);
        writer.Fixed("range", look.Range);
        writer.UInt("viewers", look.Viewers);
        writer.Int("cellX", look.CellX);
        writer.Int("cellY", look.CellY);
    }

    private static LookRecord LoadLook(IStateReader reader)
        => new(reader.Fixed("x"), reader.Fixed("y"), reader.Fixed("range"), reader.UInt("viewers"), checked((int)reader.Int("cellX")), checked((int)reader.Int("cellY")));

    private void LoadLooks(IStateReader state)
    {
        _unlooks.Clear();
        if (!state.Has("looks")) return;
        foreach (var entry in state.Array("looks"))
            if (FindEntity(new EntityId(checked((uint)entry.UInt("entity")))) is { } entity) entity.Look = LoadLook(entry);
        foreach (var entry in state.Array("unlooks")) _unlooks.Enqueue((entry.Int("until"), LoadLook(entry)));
    }

    private int SlotOf(PlayerId player) => _playerSlots is not null && _playerSlots.TryGetValue(player.Value, out var slot) ? slot : -1;

    /// <summary>Is the cell under the point currently in sight of the player? Always true without a terrain or for the system authority.</summary>
    public bool IsPositionVisibleTo(PlayerId player, Fixed x, Fixed y)
        => _vision is null || player.IsSystem || (SlotOf(player) is var slot && slot >= 0 && _vision.IsVisibleAt(slot, x, y));

    /// <summary>Has the player ever seen the cell under the point? Always true without a terrain or for the system authority.</summary>
    public bool IsPositionExploredBy(PlayerId player, Fixed x, Fixed y)
        => _vision is null || player.IsSystem || (SlotOf(player) is var slot && slot >= 0 && _vision.IsExploredAt(slot, x, y));

    /// <summary>
    /// May the player see (and therefore target) the entity? Allied objects always; others only in a visible cell and, when stealthed, only while
    /// detected (<c>Object::getShroudedStatus</c> plus the <c>STEALTHED</c>/<c>DETECTED</c> test of <c>ActionManager::canAttack</c>).
    /// </summary>
    public bool CanPlayerSee(PlayerId player, Entity entity)
    {
        if (_vision is null || player.IsSystem) return true;
        if (entity.Owner == player || RelationshipOf(player, entity.Owner) == Relationship.Allies) return true;
        if (entity.ContainedBy.IsValid) return false;
        // Where the frame the object has begun ends: at 30 Hz it has taken its whole step (F-82).
        var (x, y, _) = entity.FrameEnd(_tick);
        if (!IsPositionVisibleTo(player, x, y)) return false;
        // BeaconClientUpdate::hideBeacon: the beacon of a non-ally is not there for this player.
        return !IsHiddenFrom(player, entity) && !IsBeacon(entity);
    }

    /// <summary>
    /// <c>PartitionFilterStealthedAndUndetected</c>: stealthed and undetected. A unit in disguise is no stealthed unit to a player whose enemy it looks like: it stands there as that vehicle.
    /// </summary>
    private bool IsHiddenFrom(PlayerId player, Entity entity)
    {
        if (!entity.HasStatus(StealthedStatus) || entity.HasStatus(DetectedStatus)) return false;
        if (!entity.HasStatus(DisguisedStatus) || !entity.Template.IsKindOf("DISGUISER")) return true;
        foreach (var module in entity.ModuleArray)
            if (module is Modules.BuiltIn.StealthUpdate { IsDisguised: true } stealth) return RelationshipOf(player, stealth.DisguisedPlayer) != Relationship.Enemies;
        return true;
    }

    private static readonly int DisguisedStatus = ObjectStatusBits.Index("DISGUISED");

    /// <summary>Every entity the player may see, in ascending id order: the only view a computer player is allowed to plan with.</summary>
    public List<Entity> EntitiesVisibleTo(PlayerId player)
    {
        var result = new List<Entity>();
        foreach (var entity in _entities.Values)
            if (!entity.DestroyRequested && CanPlayerSee(player, entity)) result.Add(entity);
        result.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        return result;
    }

    private void DescribeVision(IStateWriter writer)
    {
        if (_vision is null) return;
        writer.BeginArray("vision");
        for (var slot = 0; slot < _playerList.Length; slot++)
        {
            writer.BeginObject(null);
            writer.Text("cells", _vision.Save(slot));
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadVision(IStateReader state)
    {
        if (_vision is null) return;
        var slots = state.Array("vision");
        if (slots.Count != _playerList.Length) throw new InvalidDataException("Vision table size mismatch.");
        for (var slot = 0; slot < slots.Count; slot++) _vision.Load(slot, slots[slot].Text("cells"));
    }
}

/// <summary>Where an object looked from, how far, for whom, and the partition cell it stood in then.</summary>
internal sealed record LookRecord(Fixed X, Fixed Y, Fixed Range, ulong Viewers, int CellX, int CellY);

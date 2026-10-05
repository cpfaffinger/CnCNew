using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

/// <summary>
/// <paramref name="MaxSelectionSize"/>: <c>InGameUI.ini MaxSelectionSize</c> (retail 0 = unlimited), enforced by select-all and select-matching as in
/// the original. <paramref name="GroupDoubleTapMilliseconds"/>: the second press of a group key within this time centres the camera
/// (<c>GlobalData::m_doubleClickTimeMS</c>, the Windows double-click time).
/// </summary>
public sealed record SelectionSettings(int MaxSelectionSize = 0, float MinPickRadiusPixels = 8f, long GroupDoubleTapMilliseconds = 500);

/// <summary>
/// The local player's selection and control groups (<c>SelectionXlat.cpp</c>, <c>InGameUI.cpp</c> selection helpers). Own objects form a
/// commandable selection; a single enemy, neutral or allied object may be selected for information only (<see cref="IsCommandable"/> false).
/// The selection is kept in ascending id order, so every command derived from it is issued in a canonical order.
/// </summary>
public sealed class SelectionModel
{
    private readonly SortedSet<uint> _selected = [];
    private readonly SortedSet<uint>[] _groups = [..Enumerable.Range(0, 10).Select(_ => new SortedSet<uint>())];
    private int _lastGroup = -1;
    private long _lastGroupTime;
    private bool _hasLastGroupTime;

    public SelectionModel(PlayerId localPlayer, SelectionSettings? settings = null)
    {
        LocalPlayer = localPlayer;
        Settings = settings ?? new SelectionSettings();
        Picker = new EntityPicker(Settings.MinPickRadiusPixels);
    }

    public PlayerId LocalPlayer { get; }
    public SelectionSettings Settings { get; }
    public EntityPicker Picker { get; }

    /// <summary>Raised with the new selection whenever it changes.</summary>
    public event Action<ImmutableArray<EntityId>>? SelectionChanged;

    public ImmutableArray<EntityId> Selected => [.._selected.Select(id => new EntityId(id))];
    public bool IsEmpty => _selected.Count == 0;
    public bool Contains(EntityId id) => _selected.Contains(id.Value);

    /// <summary>True when something is selected and every selected object belongs to the local player.</summary>
    public bool IsCommandable(SimulationWorld world) => _selected.Count > 0 && _selected.All(id => world.FindEntity(new EntityId(id)) is { } entity && entity.Owner == LocalPlayer);

    public ImmutableArray<EntityId> Group(int index) => [.._groups[index].Select(id => new EntityId(id))];

    /// <summary>The live, own selected entities in id order.</summary>
    public ImmutableArray<Entity> OwnSelected(SimulationWorld world)
        => [.._selected.Select(id => world.FindEntity(new EntityId(id))).OfType<Entity>().Where(entity => entity.Owner == LocalPlayer && !entity.DestroyRequested)];

    public void Clear() => Replace([]);

    public void Replace(IEnumerable<EntityId> ids)
    {
        var next = new SortedSet<uint>(ids.Select(id => id.Value));
        if (Picker.ScriptUnselectableCount > 0) next.RemoveWhere(Picker.IsScriptUnselectable);
        if (next.SetEquals(_selected)) return;
        _selected.Clear();
        _selected.UnionWith(next);
        SelectionChanged?.Invoke(Selected);
    }

    private void Change(Action<SortedSet<uint>> edit)
    {
        var before = new SortedSet<uint>(_selected);
        edit(_selected);
        if (Picker.ScriptUnselectableCount > 0) _selected.RemoveWhere(Picker.IsScriptUnselectable);
        if (!before.SetEquals(_selected)) SelectionChanged?.Invoke(Selected);
    }

    private enum Side { Mine, Friends, Civilians, Enemies }

    private Side SideOf(SimulationWorld world, Entity entity)
    {
        if (entity.Owner == LocalPlayer) return Side.Mine;
        return world.RelationshipOf(LocalPlayer, entity.Owner) switch
        {
            Relationship.Allies => Side.Friends,
            Relationship.Neutral => Side.Civilians,
            _ => Side.Enemies,
        };
    }

    /// <summary>
    /// <c>SelectionTranslator::onMouseLeftClick</c> for a point or a box, once the router has decided that the click is not an order.
    /// <paramref name="preferSelection"/> is the shift state (<c>isInPreferSelectionMode</c>). Returns false when nothing qualifies (the original keeps
    /// the message, which may become a move order).
    /// </summary>
    public bool SelectFromCandidates(SimulationWorld world, ImmutableArray<Entity> candidates, bool isPoint, bool preferSelection)
    {
        if (candidates.IsEmpty) return false;
        var addToGroup = preferSelection;
        // A selection holding foreign objects or one of my buildings is never extended.
        foreach (var id in _selected)
            if (world.FindEntity(new EntityId(id)) is { } current && (SideOf(world, current) != Side.Mine || current.Template.IsKindOf("STRUCTURE"))) addToGroup = false;

        var mine = candidates.Where(entity => SideOf(world, entity) == Side.Mine).ToList();
        var mineBuildings = mine.Count(entity => entity.Template.IsKindOf("STRUCTURE"));
        var counts = candidates.GroupBy(entity => SideOf(world, entity)).ToDictionary(group => group.Key, group => group.Count());
        var selectMine = false;
        var selectMineBuildings = false;
        Side? selectOther = null;
        if (mine.Count > 0)
        {
            selectMine = true;
            // Exactly one clicked or dragged building; or one building and nothing else selectable in the region.
            if (mineBuildings == 1 && (mine.Count == 1 || candidates.Length == 1))
            {
                addToGroup = false;
                selectMineBuildings = true;
            }
        }
        else if (counts.ContainsKey(Side.Enemies) && counts.ContainsKey(Side.Civilians) && counts.ContainsKey(Side.Friends)) return false;
        else if (counts.GetValueOrDefault(Side.Enemies) == 1) selectOther = Side.Enemies;
        else if (counts.GetValueOrDefault(Side.Civilians) == 1) selectOther = Side.Civilians;
        else if (counts.GetValueOrDefault(Side.Friends) == 1) selectOther = Side.Friends;
        if (!selectMine && selectOther is null) return false;
        if (selectOther is not null) addToGroup = false;

        _lastGroup = -1;
        if (preferSelection && isPoint && candidates.All(entity => _selected.Contains(entity.Id.Value)))
        {
            Change(set => { foreach (var entity in candidates) set.Remove(entity.Id.Value); });
            return true;
        }
        Change(set =>
        {
            if (!addToGroup) set.Clear();
            foreach (var entity in candidates)
            {
                var side = SideOf(world, entity);
                if (selectMine && side == Side.Mine && (!entity.Template.IsKindOf("STRUCTURE") || selectMineBuildings)) set.Add(entity.Id.Value);
                else if (selectOther == side) set.Add(entity.Id.Value);
            }
        });
        return true;
    }

    /// <summary>A point click: the picked object as the only candidate.</summary>
    public bool ClickSelect(SimulationWorld world, IViewProjection view, Vector2 screen, bool preferSelection)
    {
        var picked = Picker.Pick(world, LocalPlayer, view, screen);
        return picked is not null && SelectFromCandidates(world, [picked], isPoint: true, preferSelection);
    }

    public bool BoxSelect(SimulationWorld world, IViewProjection view, ScreenRect rect, bool preferSelection)
        => SelectFromCandidates(world, Picker.InRect(world, LocalPlayer, view, rect), isPoint: false, preferSelection);

    /// <summary>
    /// <c>SelectionTranslator::onMouseLeftDoubleClick</c>: an own mass-selectable object under the cursor is selected together with every matching
    /// own object on screen (<paramref name="acrossMap"/>: Alt, the whole map); shift keeps the earlier mass-selectable selection.
    /// </summary>
    public bool DoubleClickSelect(SimulationWorld world, IViewProjection view, Vector2 screen, bool acrossMap, bool preferSelection)
    {
        var picked = Picker.Pick(world, LocalPlayer, view, screen);
        if (picked is null || picked.Owner != LocalPlayer || !EntityPicker.IsMassSelectable(picked)) return false;
        var keep = preferSelection
            ? _selected.Select(id => world.FindEntity(new EntityId(id))).OfType<Entity>().Where(entity => entity.Owner == LocalPlayer && EntityPicker.IsMassSelectable(entity)).Select(entity => entity.Id.Value).ToList()
            : [];
        _lastGroup = -1;
        Change(set =>
        {
            set.Clear();
            set.UnionWith(keep);
            set.Add(picked.Id.Value);
        });
        SelectMatching(world, acrossMap ? null : view);
        return true;
    }

    /// <summary>
    /// <c>InGameUI::selectMatchingAcrossRegion</c>: adds every own, uncontained, mass-selectable object whose template equals a selected own object's
    /// template, on screen (<paramref name="view"/>) or across the map (null). Returns the number added.
    /// </summary>
    public int SelectMatching(SimulationWorld world, IViewProjection? view)
    {
        var templates = OwnSelected(world).Select(entity => entity.Template.Name).ToHashSet(StringComparer.Ordinal);
        if (templates.Count == 0) return 0;
        var pool = view is null ? world.Entities.OrderBy(entity => entity.Id.Value).ToImmutableArray() : Picker.OnScreen(world, LocalPlayer, view);
        return AddUpToLimit(pool.Where(entity => entity.Owner == LocalPlayer && EntityPicker.IsMassSelectable(entity) && templates.Contains(entity.Template.Name)));
    }

    /// <summary><c>MSG_META_SELECT_MATCHING_UNITS</c> (<c>selectUnitsMatchingCurrentSelection</c>): the screen first, the whole map when the screen added nothing. -1: nothing own is selected.</summary>
    public int SelectMatchingUnits(SimulationWorld world, IViewProjection view)
    {
        if (OwnSelected(world).IsEmpty) return -1;
        var added = SelectMatching(world, view);
        return added > 0 ? added : SelectMatching(world, null);
    }

    /// <summary>
    /// <c>MSG_META_SELECT_ALL</c> / <c>SELECT_ALL_AIRCRAFT</c> (<c>CommandXlat.cpp</c>, <c>kindOfUnitSelection</c>): own mass-selectable objects except
    /// dozers, harvesters and <c>IGNORES_SELECT_ALL</c> (aircraft only for the aircraft variant); a selection holding anything excluded is replaced,
    /// otherwise extended. The screen first, the whole map when the screen has none.
    /// </summary>
    public int SelectAll(SimulationWorld world, IViewProjection view, bool aircraftOnly)
    {
        bool Qualifies(Entity entity) => entity.Owner == LocalPlayer && EntityPicker.IsMassSelectable(entity)
                                         && !entity.Template.IsKindOf("DOZER") && !entity.Template.IsKindOf("HARVESTER") && !entity.Template.IsKindOf("IGNORES_SELECT_ALL")
                                         && (!aircraftOnly || entity.Template.IsKindOf("AIRCRAFT"));
        _lastGroup = -1;
        if (_selected.Any(id => world.FindEntity(new EntityId(id)) is not { } entity || !Qualifies(entity))) Change(set => set.Clear());
        var added = AddUpToLimit(Picker.OnScreen(world, LocalPlayer, view).Where(Qualifies));
        return added > 0 ? added : AddUpToLimit(world.Entities.OrderBy(entity => entity.Id.Value).Where(Qualifies));
    }

    private int AddUpToLimit(IEnumerable<Entity> candidates)
    {
        var added = 0;
        Change(set =>
        {
            foreach (var entity in candidates)
            {
                if (Settings.MaxSelectionSize > 0 && set.Count >= Settings.MaxSelectionSize) break;
                if (set.Add(entity.Id.Value)) added++;
            }
        });
        return added;
    }

    /// <summary>
    /// <c>SELECT_NEXT_UNIT</c>/<c>PREV_UNIT</c> and <c>SELECT_NEXT_WORKER</c>/<c>PREV_WORKER</c>: walks the own objects in id order from the first
    /// selected one (wrapping), takes the next mobile uncontained one (a dozer for workers), selects only it and returns where to centre the camera.
    /// </summary>
    public Vector2? SelectNext(SimulationWorld world, bool workers, bool forward)
    {
        var candidates = world.Entities
            .Where(entity => entity.Owner == LocalPlayer && EntityPicker.IsSelectable(entity) && IsMobile(entity) && (!workers || entity.Template.IsKindOf("DOZER")))
            .OrderBy(entity => entity.Id.Value).ToList();
        if (candidates.Count == 0) return null;
        Entity next;
        if (_selected.Count == 0) next = forward ? candidates[0] : candidates[^1];
        else
        {
            var anchor = _selected.Min;
            next = forward
                ? candidates.FirstOrDefault(entity => entity.Id.Value > anchor) ?? candidates[0]
                : candidates.LastOrDefault(entity => entity.Id.Value < anchor) ?? candidates[^1];
        }
        _lastGroup = -1;
        Replace([next.Id]);
        var center = EntityPicker.CenterOf(next);
        return new Vector2(center.X, center.Y);
    }

    private static bool IsMobile(Entity entity)
        => !entity.Template.IsKindOf("STRUCTURE") && !entity.Template.IsKindOf("IMMOBILE") && entity.Modules.Any(module => module is AIUpdateInterface);

    /// <summary>
    /// Control groups (<c>onMetaCreateTeam</c>, <c>onMetaSelectTeam</c>, <c>onMetaAddTeam</c>, <c>onMetaViewTeam</c>, <c>Player::processCreateTeamGameMessage</c>).
    /// Returns a camera centre when the action asks for one. <paramref name="timeMs"/> is the caller's clock for the double-press test.
    /// </summary>
    public Vector2? TeamAction(SimulationWorld world, TeamActionKind kind, int group, long timeMs)
    {
        if (group is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(group));
        PruneGroups(world);
        var members = _groups[group];
        Vector2? LastMember()
        {
            if (members.Count == 0 || world.FindEntity(new EntityId(members.Max)) is not { } last) return null;
            var center = EntityPicker.CenterOf(last);
            return new Vector2(center.X, center.Y);
        }
        switch (kind)
        {
            case TeamActionKind.Create:
            {
                var own = OwnSelected(world).Select(entity => entity.Id.Value).ToList();
                foreach (var other in _groups) other.ExceptWith(own);
                members.Clear();
                members.UnionWith(own);
                return null;
            }
            case TeamActionKind.View:
                return LastMember();
            case TeamActionKind.Select or TeamActionKind.Add:
            {
                if (!_hasLastGroupTime) { _lastGroupTime = timeMs; _hasLastGroupTime = true; }
                var doubleTap = timeMs - _lastGroupTime < Settings.GroupDoubleTapMilliseconds && group == _lastGroup;
                _lastGroupTime = timeMs;
                _lastGroup = group;
                if (kind == TeamActionKind.Select)
                {
                    Vector2? center = null;
                    var perform = true;
                    if (doubleTap && members.Count > 0)
                    {
                        center = LastMember();
                        perform = !members.All(_selected.Contains);
                    }
                    if (perform) Replace(members.Where(id => world.FindEntity(new EntityId(id))?.Owner == LocalPlayer).Select(id => new EntityId(id)));
                    return center;
                }
                if (doubleTap) return LastMember();
                Change(set =>
                {
                    var firstSelected = set.Count > 0 ? world.FindEntity(new EntityId(set.Min)) : null;
                    var firstMember = members.Count > 0 ? world.FindEntity(new EntityId(members.Min)) : null;
                    if (firstSelected?.Template.IsKindOf("STRUCTURE") == true || firstMember?.Template.IsKindOf("STRUCTURE") == true) set.Clear();
                    set.UnionWith(members);
                });
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Drops what may no longer be selected (<c>GameLogic::deselectObject</c> callers): removed, dead, contained, unselectable, sold, changed owner
    /// (an own object that became foreign), and foreign objects the local player can no longer see. Groups lose removed and foreign objects.
    /// </summary>
    public void Prune(SimulationWorld world)
    {
        PruneGroups(world);
        Change(set =>
        {
            var count = set.Count;
            set.RemoveWhere(id =>
            {
                var entity = world.FindEntity(new EntityId(id));
                if (entity is null || !EntityPicker.IsSelectable(entity) || entity.HasStatus(ObjectStatusBits.Sold)) return true;
                // A foreign object is only ever selected alone, for information, and only while it stays in sight.
                return entity.Owner != LocalPlayer && (count > 1 || !world.CanPlayerSee(LocalPlayer, entity));
            });
        });
    }

    private void PruneGroups(SimulationWorld world)
    {
        foreach (var group in _groups)
            group.RemoveWhere(id => world.FindEntity(new EntityId(id)) is not { } entity || entity.DestroyRequested || entity.EffectivelyDead || entity.Owner != LocalPlayer);
    }
}

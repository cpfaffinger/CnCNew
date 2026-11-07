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
/// What the original tells the player when it selects more units at once (<c>InGameUI::selectMatchingAcrossScreen</c>, <c>selectMatchingAcrossMap</c>,
/// <c>selectAllUnitsByTypeAcrossScreen</c>, <c>selectAllUnitsByTypeAcrossMap</c>): a line in the message list at the top left.
/// </summary>
public enum SelectionAnnouncement
{
    /// <summary><c>GUI:NothingSelected</c>: there was nothing of the player's own to find more of.</summary>
    NothingSelected,
    /// <summary><c>GUI:SelectedAcrossScreen</c>: units were added from the screen.</summary>
    AcrossScreen,
    /// <summary><c>GUI:SelectedAcrossMap</c>: the search went over the whole map.</summary>
    AcrossMap,
}

/// <summary>
/// The local player's selection and control groups (<c>SelectionXlat.cpp</c>, <c>InGameUI.cpp</c> selection helpers). Own objects form a
/// commandable selection; a single enemy, neutral or allied object may be selected for information only (<see cref="IsCommandable"/> false).
/// The selection is kept in ascending id order, so every command derived from it is issued in a canonical order.
/// </summary>
public sealed class SelectionModel
{
    private readonly SortedSet<uint> _selected = [];
    private readonly SortedSet<uint>[] _groups = [..Enumerable.Range(0, 10).Select(_ => new SortedSet<uint>())];
    private readonly List<uint> _idleWorkers = [];
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

    /// <summary>Raised when a selection of many units (the matching units, select all, a double click) tells the player what it did (<see cref="SelectionAnnouncement"/>).</summary>
    public event Action<SelectionAnnouncement>? Announced;

    public ImmutableArray<EntityId> Selected => [.._selected.Select(id => new EntityId(id))];
    public bool IsEmpty => _selected.Count == 0;
    public bool Contains(EntityId id) => _selected.Contains(id.Value);

    /// <summary>True when something is selected and every selected object belongs to the local player.</summary>
    public bool IsCommandable(SimulationWorld world) => _selected.Count > 0 && _selected.All(id => world.FindEntity(new EntityId(id)) is { } entity && entity.Owner == LocalPlayer);

    public ImmutableArray<EntityId> Group(int index) => [.._groups[index].Select(id => new EntityId(id))];

    /// <summary>
    /// The control group an object belongs to, 0 to 9, or -1 for none (<c>Player::getSquadNumberForObject</c>: the lowest squad that holds it; creating a group takes its members out of
    /// every other, so an object is in one at most).
    /// </summary>
    public int GroupOf(EntityId id)
    {
        for (var index = 0; index < _groups.Length; index++)
            if (_groups[index].Contains(id.Value)) return index;
        return -1;
    }

    /// <summary>The live, own selected entities in id order.</summary>
    public ImmutableArray<Entity> OwnSelected(SimulationWorld world)
        => [.._selected.Select(id => world.FindEntity(new EntityId(id))).OfType<Entity>().Where(entity => entity.Owner == LocalPlayer && !entity.DestroyRequested)];

    public void Clear() => Replace([]);

    // ------------------------------------------------------------------------------------------------ subgroups

    /// <summary>
    /// The template of the active subgroup of a selection that holds several kinds of object (Tab, a feature of this client after StarCraft), or null while the bar shows what it
    /// always showed for such a selection - the buttons all the selected objects share. Kept while the selection only shrinks; any other change of the selection resets it. A name
    /// that is no longer a subgroup of the selection is not active (<see cref="ActiveSubgroupIn"/>).
    /// </summary>
    public string? ActiveSubgroup { get; private set; }

    /// <summary>Raised when Tab changed the active subgroup: a pending targeting mode belonged to the earlier one.</summary>
    public event Action? SubgroupChanged;

    /// <summary>
    /// The kinds of the selected objects in the order Tab walks them (template names, ordinal; the order of the plates of the modern bar), when the selection is the player's own and holds
    /// at least two kinds; empty otherwise - a selection of one kind has no subgroups.
    /// </summary>
    public ImmutableArray<string> Subgroups(SimulationWorld world)
    {
        var own = OwnSelected(world);
        if (own.Length < 2 || own.Length != _selected.Count) return [];
        var kinds = own.Select(entity => entity.Template.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        return kinds.Length >= 2 ? kinds : [];
    }

    /// <summary><see cref="ActiveSubgroup"/> if it still names one of the selection's kinds, otherwise null.</summary>
    public string? ActiveSubgroupIn(SimulationWorld world)
        => ActiveSubgroup is { } active && Subgroups(world).Contains(active, StringComparer.Ordinal) ? active : null;

    /// <summary>The own selected objects of one kind, in selection order.</summary>
    public ImmutableArray<Entity> SubgroupMembers(SimulationWorld world, string template)
        => [..OwnSelected(world).Where(entity => entity.Template.Name.Equals(template, StringComparison.Ordinal))];

    /// <summary>
    /// Tab (<paramref name="forward"/>) and Shift+Tab: the next or the earlier kind of a selection of several kinds. The walk is a ring: it begins on the buttons all objects share, goes
    /// through the kinds one by one and returns there after the last. False, doing nothing, when the selection has no subgroups.
    /// </summary>
    public bool CycleSubgroup(SimulationWorld world, bool forward)
    {
        var kinds = Subgroups(world);
        if (kinds.IsEmpty) return false;
        var current = ActiveSubgroupIn(world) is { } active ? kinds.IndexOf(active, StringComparer.Ordinal) : -1;
        var next = current + (forward ? 1 : -1);
        if (next >= kinds.Length) next = -1;
        else if (next < -1) next = kinds.Length - 1;
        ActiveSubgroup = next < 0 ? null : kinds[next];
        SubgroupChanged?.Invoke();
        return true;
    }

    public void Replace(IEnumerable<EntityId> ids)
    {
        var next = new SortedSet<uint>(ids.Select(id => id.Value));
        if (Picker.ScriptUnselectableCount > 0) next.RemoveWhere(Picker.IsScriptUnselectable);
        if (next.SetEquals(_selected)) return;
        if (!next.IsSubsetOf(_selected)) ActiveSubgroup = null;
        _selected.Clear();
        _selected.UnionWith(next);
        SelectionChanged?.Invoke(Selected);
    }

    private void Change(Action<SortedSet<uint>> edit)
    {
        var before = new SortedSet<uint>(_selected);
        edit(_selected);
        if (Picker.ScriptUnselectableCount > 0) _selected.RemoveWhere(Picker.IsScriptUnselectable);
        if (before.SetEquals(_selected)) return;
        if (!_selected.IsSubsetOf(before)) ActiveSubgroup = null;
        SelectionChanged?.Invoke(Selected);
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
        // A Shift-click on one more building of the kind that is selected joins it to the structure group (an addition of this client; the original never extends a building selection).
        if (preferSelection && isPoint && selectMineBuildings && candidates.Length == 1 && JoinsStructureGroup(world, mine[0])) addToGroup = true;

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

    /// <summary>
    /// True when <paramref name="building"/> is a finished own structure of the template every selected object has: the selection is a structure group (or one building) of its kind.
    /// </summary>
    private bool JoinsStructureGroup(SimulationWorld world, Entity building)
        => IsGroupableStructure(building) && _selected.Count > 0 && OwnStructureTemplate(world) == building.Template.Name;

    /// <summary>
    /// A structure that may be part of a group of its kind: selectable, finished and not being sold (the callers check the owner). A site has no command set of its own to share, so
    /// it is never taken along by a double click or by the matching keys.
    /// </summary>
    public static bool IsGroupableStructure(Entity entity)
        => EntityPicker.IsSelectable(entity) && entity.Template.IsKindOf("STRUCTURE")
           && !entity.HasStatus(ObjectStatusBits.UnderConstruction) && !entity.HasStatus(ObjectStatusBits.Sold);

    /// <summary>
    /// The template name when everything selected is a structure of the player's own and they are all of one template (one building counts), otherwise null. Two or more such
    /// buildings are a structure group: they take the same orders at once (<see cref="CommandBarModel"/>).
    /// </summary>
    public string? OwnStructureTemplate(SimulationWorld world)
    {
        string? template = null;
        foreach (var id in _selected)
        {
            if (world.FindEntity(new EntityId(id)) is not { } entity || entity.Owner != LocalPlayer || !entity.Template.IsKindOf("STRUCTURE")) return null;
            if (template is null) template = entity.Template.Name;
            else if (!template.Equals(entity.Template.Name, StringComparison.Ordinal)) return null;
        }
        return template;
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
        if (picked is null || picked.Owner != LocalPlayer) return false;
        if (!EntityPicker.IsMassSelectable(picked)) return picked.Template.IsKindOf("STRUCTURE") && DoubleClickStructure(world, view, picked, acrossMap);
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
        // SelectionXlat: selectMatchingAcrossMap / selectMatchingAcrossScreen, each with its message.
        if (acrossMap) MatchingAcrossMap(world);
        else MatchingAcrossScreen(world, view);
        return true;
    }

    /// <summary>
    /// The double click on one of the player's own buildings (a feature of this client; the original double-clicks only units): the building and every finished building of its
    /// template on the screen (Alt: the whole map) are selected as a structure group. The first click of the pair must have selected the building; one that an order went to instead
    /// (a dozer's repair under the left-click scheme) is not reselected.
    /// </summary>
    private bool DoubleClickStructure(SimulationWorld world, IViewProjection view, Entity picked, bool acrossMap)
    {
        if (!IsGroupableStructure(picked) || !_selected.Contains(picked.Id.Value) || OwnStructureTemplate(world) != picked.Template.Name) return false;
        _lastGroup = -1;
        Change(set =>
        {
            set.Clear();
            set.Add(picked.Id.Value);
        });
        if (acrossMap) MatchingAcrossMap(world);
        else MatchingAcrossScreen(world, view);
        return true;
    }

    /// <summary>
    /// <c>InGameUI::selectMatchingAcrossRegion</c>: adds every own, uncontained, mass-selectable object whose template equals a selected own object's
    /// template, on screen (<paramref name="view"/>) or across the map (null). Returns the number added, or -1 when nothing of the player's own is selected.
    /// A selection of buildings finds the finished buildings of its template instead (the structure group of this client; the original's call finds no structure).
    /// </summary>
    public int SelectMatching(SimulationWorld world, IViewProjection? view)
    {
        var own = OwnSelected(world);
        var templates = own.Select(entity => entity.Template.Name).ToHashSet(StringComparer.Ordinal);
        if (templates.Count == 0) return -1;
        var structures = own.All(entity => entity.Template.IsKindOf("STRUCTURE"));
        var pool = view is null ? world.Entities.OrderBy(entity => entity.Id.Value).ToImmutableArray() : Picker.OnScreen(world, LocalPlayer, view);
        return AddUpToLimit(pool.Where(entity => entity.Owner == LocalPlayer && (structures ? IsGroupableStructure(entity) : EntityPicker.IsMassSelectable(entity)) && templates.Contains(entity.Template.Name)));
    }

    /// <summary><c>InGameUI::selectMatchingAcrossScreen</c>: the matching units on screen; says "nothing selected" when there was nothing to match, "across screen" when units were added, nothing when none were.</summary>
    private int MatchingAcrossScreen(SimulationWorld world, IViewProjection view)
    {
        var added = SelectMatching(world, view);
        if (added == -1) Announced?.Invoke(SelectionAnnouncement.NothingSelected);
        else if (added > 0) Announced?.Invoke(SelectionAnnouncement.AcrossScreen);
        return added;
    }

    /// <summary>
    /// <c>InGameUI::selectMatchingAcrossMap</c>: the matching units of the whole map. Says "across map" when units were added, and also when none were - unless the first selected
    /// object is a structure, which has no fellows to look for across the map.
    /// </summary>
    private int MatchingAcrossMap(SimulationWorld world)
    {
        var added = SelectMatching(world, null);
        if (added == -1) Announced?.Invoke(SelectionAnnouncement.NothingSelected);
        else if (added > 0 || !FirstSelectedIsStructure(world)) Announced?.Invoke(SelectionAnnouncement.AcrossMap);
        return added;
    }

    private bool FirstSelectedIsStructure(SimulationWorld world)
        => _selected.Count > 0 && world.FindEntity(new EntityId(_selected.Min)) is { } first && first.Template.IsKindOf("STRUCTURE");

    /// <summary>
    /// <c>MSG_META_SELECT_MATCHING_UNITS</c> (<c>selectUnitsMatchingCurrentSelection</c>): the screen first, the whole map when the screen added nothing - each step says what it did
    /// (<see cref="Announced"/>). -1: nothing own is selected.
    /// </summary>
    public int SelectMatchingUnits(SimulationWorld world, IViewProjection view)
    {
        var onScreen = MatchingAcrossScreen(world, view);
        return onScreen == 0 ? MatchingAcrossMap(world) : onScreen;
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
        // selectAllUnitsByTypeAcrossScreen says "across screen" when it added any; selectAllUnitsByTypeAcrossMap says "across map" - when it added none as well, unless the first
        // selected object is a structure.
        if (added > 0)
        {
            Announced?.Invoke(SelectionAnnouncement.AcrossScreen);
            return added;
        }
        added = AddUpToLimit(world.Entities.OrderBy(entity => entity.Id.Value).Where(Qualifies));
        if (added > 0 || !FirstSelectedIsStructure(world)) Announced?.Invoke(SelectionAnnouncement.AcrossMap);
        return added;
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

    /// <summary>The local player's idle workers, in the order they became idle (<c>InGameUI::m_idleWorkers</c>).</summary>
    public int IdleWorkerCount => _idleWorkers.Count;

    /// <summary>
    /// <c>DozerPrimaryIdleState::update</c> with <c>InGameUI::addIdleWorker</c>/<c>removeIdleWorker</c>: a <c>DOZER</c> of the local player whose AI is idle and that is not
    /// effectively dead joins the end of the list, one that is busy, dead or another player's leaves it. Called once a frame; the ones that became idle in the same frame
    /// join in id order.
    /// </summary>
    public int TrackIdleWorkers(SimulationWorld world)
    {
        _idleWorkers.RemoveAll(id => world.FindEntity(new EntityId(id)) is not { } worker || !IsIdleWorker(world, worker));
        foreach (var entity in world.EntitiesOwnedBy(LocalPlayer).AsSpan())
            if (entity.Template.IsKindOf("DOZER") && IsIdleWorker(world, entity) && !_idleWorkers.Contains(entity.Id.Value)) _idleWorkers.Add(entity.Id.Value);
        return _idleWorkers.Count;
    }

    private bool IsIdleWorker(SimulationWorld world, Entity entity)
        => entity.Owner == LocalPlayer && !entity.EffectivelyDead && !entity.DestroyRequested && entity.Modules.Any(module => module is AIUpdateInterface) && world.IsAiIdle(entity);

    /// <summary>
    /// <c>InGameUI::selectNextIdleWorker</c>: with nothing or several things selected, the first idle worker; with one selected, the idle worker after it (the first after the
    /// last). A worker inside something is selected as its outermost container, and a container holding several counts once (<c>getUniqueIdleWorkers</c>). Returns where the
    /// camera looks (<c>userLookAt</c>), or null when no worker is idle.
    /// </summary>
    public Vector2? SelectNextIdleWorker(SimulationWorld world)
    {
        TrackIdleWorkers(world);
        if (_idleWorkers.Count == 0) return null;
        Entity Outermost(Entity entity)
        {
            while (entity.ContainedBy.IsValid && world.FindEntity(entity.ContainedBy) is { } container) entity = container;
            return entity;
        }
        var unique = new List<Entity>();
        foreach (var id in _idleWorkers)
            if (world.FindEntity(new EntityId(id)) is { } worker && Outermost(worker) is var top && !unique.Contains(top)) unique.Add(top);
        if (unique.Count == 0) return null;
        var next = unique[0];
        if (_selected.Count == 1)
        {
            var at = unique.FindIndex(entity => entity.Id.Value == _selected.Min);
            if (at >= 0) next = unique[(at + 1) % unique.Count];
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
                    // A building that is selected or stands first in the group empties the selection, unless it and the members are finished buildings of one kind (a structure group).
                    if ((firstSelected?.Template.IsKindOf("STRUCTURE") == true || firstMember?.Template.IsKindOf("STRUCTURE") == true) && !SameStructureKind(world, set, members)) set.Clear();
                    set.UnionWith(members);
                });
                return null;
            }
        }
        return null;
    }

    /// <summary>True when everything in both sets is a finished structure of the player's own and they all share one template (both sets non-empty).</summary>
    private bool SameStructureKind(SimulationWorld world, SortedSet<uint> selected, SortedSet<uint> members)
    {
        if (selected.Count == 0 || members.Count == 0) return false;
        string? template = null;
        foreach (var id in selected.Concat(members))
        {
            if (world.FindEntity(new EntityId(id)) is not { } entity || entity.Owner != LocalPlayer || !IsGroupableStructure(entity)) return false;
            if (template is null) template = entity.Template.Name;
            else if (!template.Equals(entity.Template.Name, StringComparison.Ordinal)) return false;
        }
        return true;
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

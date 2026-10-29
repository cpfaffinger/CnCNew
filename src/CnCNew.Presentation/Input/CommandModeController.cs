using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

/// <summary>
/// <paramref name="PlacementDragPixels"/>: <c>PLACEMENT_DRAG_THRESHOLD_DIST</c> before a held placement click starts rotating the ghost.
/// <paramref name="MoveHintFrames"/>: lifetime of the move marker (<c>W3DInGameUI::drawMoveHints</c>).
/// <paramref name="PlacementViewAngle"/>: the heading in radians a structure template has before the player drags a direction (<c>ThingTemplate::getPlacementViewAngle</c>,
/// the <c>PlacementViewAngle</c> of the INI; <c>InGameUI::placeBuildAvailable</c> starts the placement icon with it and a click without a drag places the building with it); null: 0 for every template.
/// </summary>
public sealed record CommandSettings(float PlacementDragPixels = 5f, int MoveHintFrames = 40, int MarkerFrames = 40, Func<string, float>? PlacementViewAngle = null);

/// <summary>
/// Turns the selection, the object and ground under the cursor and the modifier states into typed simulation commands and a cursor
/// (<c>CommandXlat.cpp</c> <c>evaluateContextCommand</c>, <c>GUICommandTranslator.cpp</c>, <c>PlaceEventTranslator.cpp</c>,
/// <c>ControlBarCommandProcessing.cpp</c> <c>processCommandUI</c>). It only ever talks to the world through the <see cref="ICommandSink"/>;
/// reading the world is allowed, changing it is not.
/// </summary>
public sealed class CommandModeController
{
    private readonly ICommandSink _sink;
    private readonly List<CommandFeedback> _feedback = [];
    private CommandButtonInfo? _button;
    private ImmutableArray<EntityId> _sources = [];
    private EntityId _builder;
    private Vector3? _anchor;
    private Vector2 _anchorScreen;
    private float _placementAngle;
    private bool _rotating;

    public CommandModeController(PlayerId player, ICommandSink sink, SelectionModel selection, CommandSettings? settings = null)
    {
        Player = player;
        _sink = sink;
        Selection = selection;
        Settings = settings ?? new CommandSettings();
    }

    public PlayerId Player { get; }
    public SelectionModel Selection { get; }
    public CommandSettings Settings { get; }
    public StandingOrders Orders { get; } = new();

    public CommandModeKind Mode { get; private set; }
    public CommandButtonInfo? PendingButton => _button;
    public PlacementGhost? Ghost { get; private set; }
    public CursorKind Cursor { get; private set; } = CursorKind.Normal;

    /// <summary><c>BEGIN/END_FORCEATTACK</c> (Ctrl), <c>BEGIN/END_WAYPOINTS</c> (Alt), <c>BEGIN/END_PREFER_SELECTION</c> (Shift), <c>BEGIN/END_FORCEMOVE</c>.</summary>
    public bool ForceAttack { get; private set; }
    public bool WaypointMode { get; private set; }
    public bool PreferSelection { get; private set; }
    public bool ForceMove { get; private set; }

    /// <summary>Orders the original can give but the simulation cannot execute yet, by kind (reported through <see cref="FeedbackKind.Unsupported"/> as well).</summary>
    public SortedDictionary<string, int> UnsupportedCounts { get; } = new(StringComparer.Ordinal);

    public ImmutableArray<CommandFeedback> DrainFeedback()
    {
        var result = _feedback.ToImmutableArray();
        _feedback.Clear();
        return result;
    }

    // ------------------------------------------------------------------------------------------------ modifiers and metas

    /// <summary>Handles the command metas; false for anything else.</summary>
    public bool OnMeta(SimulationWorld world, MetaAction action)
    {
        switch (action)
        {
            case MetaAction.BeginForceAttack: ForceAttack = true; return true;
            case MetaAction.EndForceAttack: ForceAttack = false; return true;
            case MetaAction.BeginWaypoints: WaypointMode = true; return true;
            case MetaAction.EndWaypoints: WaypointMode = false; return true;
            case MetaAction.BeginPreferSelection: PreferSelection = true; return true;
            case MetaAction.EndPreferSelection: PreferSelection = false; return true;
            case MetaAction.BeginForceMove: ForceMove = true; return true;
            case MetaAction.EndForceMove: ForceMove = false; return true;
            case MetaAction.Stop: Stop(world, Selection.OwnSelected(world)); return true;
            case MetaAction.Scatter: Scatter(world, Selection.OwnSelected(world)); return true;
            case MetaAction.AttackMove: EnterSimpleMode(world, CommandModeKind.AttackMove); return true;
            case MetaAction.Guard: EnterSimpleMode(world, CommandModeKind.Guard); return true;
            default: return false;
        }
    }

    public void ReleaseModifiers() => ForceAttack = WaypointMode = PreferSelection = ForceMove = false;

    private void EnterSimpleMode(SimulationWorld world, CommandModeKind mode)
    {
        if (!Selection.IsCommandable(world)) return;
        Cancel();
        Mode = mode;
        _sources = [..Selection.OwnSelected(world).Select(entity => entity.Id)];
    }

    /// <summary>Leaves any pending targeting or placement mode (right click, Escape, selection change).</summary>
    public void Cancel()
    {
        Mode = CommandModeKind.None;
        _button = null;
        _sources = [];
        _builder = EntityId.Invalid;
        _anchor = null;
        _rotating = false;
        Ghost = null;
    }

    // ------------------------------------------------------------------------------------------------ context evaluation

    /// <summary>The cursor and the action a click at <paramref name="screen"/> would perform in the current mode.</summary>
    public ContextHint Hover(SimulationWorld world, IViewProjection view, Vector2 screen)
    {
        var target = Selection.Picker.Pick(world, Player, view, screen);
        Vector3? ground = view.TryScreenToGround(screen, out var point) ? point : null;
        ContextHint hint;
        switch (Mode)
        {
            case CommandModeKind.Placement:
                UpdateGhost(world, view, screen);
                hint = new ContextHint(ContextAction.None, Ghost is { IsValid: true } ? CursorKind.Build : CursorKind.InvalidBuild, EntityId.Invalid, ground);
                break;
            case CommandModeKind.AttackMove:
                hint = new ContextHint(ContextAction.Move, ground is null ? CursorKind.GenericInvalid : CursorKind.AttackMove, EntityId.Invalid, ground);
                break;
            case CommandModeKind.Guard:
                hint = new ContextHint(ContextAction.Move, ground is null ? CursorKind.GenericInvalid : CursorKind.Target, EntityId.Invalid, ground);
                break;
            case CommandModeKind.ButtonTarget:
            {
                var valid = ResolveButtonTarget(world, target, ground, out _, out _);
                var button = _button!;
                var cursor = valid
                    ? CursorNames.TryParse(button.CursorName, out var named) ? named : CursorKind.Target
                    : CursorNames.TryParse(button.InvalidCursorName, out var invalid) ? invalid : CursorKind.GenericInvalid;
                hint = new ContextHint(valid ? ContextAction.None : ContextAction.Invalid, cursor, target?.Id ?? EntityId.Invalid, ground);
                break;
            }
            default:
                hint = Evaluate(world, target, ground);
                break;
        }
        Cursor = hint.Cursor;
        return hint;
    }

    /// <summary><c>evaluateContextCommand</c> for the own selection against an object and/or a ground point, in the reference's order of checks.</summary>
    public ContextHint Evaluate(SimulationWorld world, Entity? target, Vector3? ground)
    {
        var own = Selection.OwnSelected(world);
        var targetId = target?.Id ?? EntityId.Invalid;
        ContextHint Hint(ContextAction action, CursorKind cursor) => new(action, cursor, targetId, ground);
        if (own.IsEmpty || !Selection.IsCommandable(world))
            return target is null ? Hint(ContextAction.None, CursorKind.Normal) : Hint(ContextAction.Select, CursorKind.Select);
        if (target is not null && target.Owner == Player && PreferSelection) return Hint(ContextAction.Select, CursorKind.Select);

        var movers = own.Where(SimulationQueries.CanMove).ToList();
        if (WaypointMode && ground is not null && movers.Count > 0) return Hint(ContextAction.AddWaypoint, CursorKind.Waypoint);

        if (ForceAttack)
        {
            var armed = own.Any(entity => SimulationQueries.CanTakeAttackOrder(world, entity));
            if (target is not null && target.Body is not null && armed && !own.Contains(target)) return Hint(ContextAction.ForceAttackObject, CursorKind.ForceAttackObj);
            if (target is null && ground is not null && armed) return Hint(ContextAction.ForceAttackGround, CursorKind.ForceAttackGround);
        }
        else if (target is not null)
        {
            if (ResumeBuilders(own, target).Any()) return Hint(ContextAction.ResumeConstruction, CursorKind.ResumeConstruction);
            if (CanDockAll(own, target)) return Hint(ContextAction.Dock, CursorKind.Dock);
            if (RepairBuilders(own, target).Any()) return Hint(ContextAction.Repair, CursorKind.DoRepair);
            if (RepairPadCandidates(world, own, target).Any()) return Hint(ContextAction.GetRepaired, CursorKind.GetRepaired);
            if (Entrants(world, own, target).Any()) return Hint(ContextAction.Enter, CursorKind.EnterFriendly);
        }

        if (target is not null && world.AreEnemies(Player, target.Owner) && target.Body is not null)
        {
            var armed = own.Where(entity => SimulationQueries.CanTakeAttackOrder(world, entity)).ToList();
            if (armed.Any(entity => SimulationQueries.InRangeOrRiders(world, entity, target))) return Hint(ContextAction.AttackObject, CursorKind.AttackObj);
            if (armed.Any(SimulationQueries.CanMove)) return Hint(ContextAction.AttackObjectAfterMoving, CursorKind.OutRange);
            return Hint(ContextAction.Invalid, CursorKind.GenericInvalid);
        }
        if (target is null && ground is not null && own.All(entity => SimulationQueries.ProductionOf(entity) is not null))
            return Hint(ContextAction.SetRallyPoint, CursorKind.SetRallyPoint);
        if (target is not null) return Hint(ContextAction.Select, CursorKind.Select);
        if (ground is not null && movers.Count > 0) return Hint(ContextAction.Move, CursorKind.Move);
        return Hint(ContextAction.Invalid, CursorKind.GenericInvalid);
    }

    private static IEnumerable<Entity> ResumeBuilders(ImmutableArray<Entity> own, Entity target)
        => target.Owner == own[0].Owner && target.HasStatus(ObjectStatusBits.UnderConstruction) ? own.Where(SimulationQueries.IsBuilder) : [];

    private static IEnumerable<Entity> RepairBuilders(ImmutableArray<Entity> own, Entity target)
        => target.Owner == own[0].Owner && AIUpdateInterface.IsRepairable(target)
            ? own.Where(entity => SimulationQueries.AiOf(entity) is { CanRepair: true } && !entity.HasStatus(ObjectStatusBits.UnderConstruction))
            : [];

    /// <summary><c>canDockAt</c> for every selected object (<c>SELECTION_ALL</c>): supply carriers at an own supply center or any warehouse.</summary>
    private static bool CanDockAll(ImmutableArray<Entity> own, Entity target)
    {
        if (target.HasStatus(ObjectStatusBits.UnderConstruction) || target.HasStatus(ObjectStatusBits.Sold)) return false;
        var center = SimulationQueries.HasModule(target, "SupplyCenterDockUpdate") && target.Owner == own[0].Owner;
        var warehouse = SimulationQueries.HasModule(target, "SupplyWarehouseDockUpdate");
        return (center || warehouse) && own.All(SimulationQueries.IsSupplyCarrier);
    }

    /// <summary><c>canGetRepairedAt</c>: hurt mobile objects at a finished own or allied repair dock.</summary>
    private IEnumerable<Entity> RepairPadCandidates(SimulationWorld world, ImmutableArray<Entity> own, Entity target)
    {
        if (!SimulationQueries.HasModule(target, "RepairDockUpdate") || target.HasStatus(ObjectStatusBits.UnderConstruction)) return [];
        if (target.Owner != Player && !world.AreAllies(Player, target.Owner)) return [];
        return own.Where(entity => SimulationQueries.CanMove(entity) && entity.Body is { } body && body.Health < body.MaxHealth);
    }

    /// <summary><c>ContainModuleInterface::isValidContainerFor</c> through the simulation's own container module.</summary>
    private static IEnumerable<Entity> Entrants(SimulationWorld world, ImmutableArray<Entity> own, Entity target)
        => target.Container is { } container && !own.Contains(target) ? own.Where(entity => container.IsValidContainerFor(world, entity, true)) : [];

    // ------------------------------------------------------------------------------------------------ issuing

    /// <summary>Executes the context order at <paramref name="screen"/>; true when an order went out.</summary>
    public bool IssueContext(SimulationWorld world, IViewProjection view, Vector2 screen)
    {
        var target = Selection.Picker.Pick(world, Player, view, screen);
        Vector3? ground = view.TryScreenToGround(screen, out var point) ? point : null;
        return IssueContext(world, Evaluate(world, target, ground), target);
    }

    public bool IssueContext(SimulationWorld world, ContextHint hint, Entity? target)
    {
        var own = Selection.OwnSelected(world);
        var ground = hint.Position ?? Vector3.Zero;
        switch (hint.Action)
        {
            case ContextAction.Move:
                return MoveTo(world, own.Where(SimulationQueries.CanMove), ground);
            case ContextAction.AddWaypoint:
            {
                var any = false;
                foreach (var unit in own.Where(SimulationQueries.CanMove))
                {
                    if (Orders.TryGet(unit.Id, out var order) && order.Kind == StandingOrderKind.Waypoints)
                    {
                        Orders.AddWaypoint(unit.Id, new Vector2(ground.X, ground.Y), world.Tick);
                        any = true;
                        continue;
                    }
                    if (Move(world, unit, ground)) { Orders.AddWaypoint(unit.Id, new Vector2(ground.X, ground.Y), world.Tick); any = true; }
                }
                if (any) Feedback(FeedbackKind.WaypointMarker, world, ground, EntityId.Invalid, Settings.MoveHintFrames);
                return any;
            }
            case ContextAction.AttackObject or ContextAction.AttackObjectAfterMoving or ContextAction.ForceAttackObject when target is not null:
            {
                var any = false;
                foreach (var unit in own.Where(entity => SimulationQueries.CanTakeAttackOrder(world, entity)))
                {
                    // A garrisoned building has no weapon and does not move: the one order goes to the simulation, which passes it to the riders (F-60, step 6).
                    if (!SimulationQueries.HasWeapon(unit) && !SimulationQueries.CanMove(unit))
                    {
                        if (Issue((sequence, tick) => new PursueCommand(Player, sequence, tick, unit.Id, target.Id, unit.X, unit.Y)).Accepted) any = true;
                        continue;
                    }
                    Orders.Set(unit.Id, new StandingOrder(StandingOrderKind.Attack, target.Id, SimulationQueries.GroundOf(target), [], world.Tick));
                    any = true;
                }
                if (!any) return false;
                Feedback(FeedbackKind.AttackMarker, world, EntityPicker.CenterOf(target), target.Id, Settings.MarkerFrames);
                return true;
            }
            case ContextAction.ForceAttackGround when ground is { } place:
            {
                // MSG_DO_FORCE_ATTACK_GROUND: the group attacks the ground without a limit (groupAttackPosition).
                var armed = own.Where(entity => SimulationQueries.CanTakeAttackOrder(world, entity)).Select(entity => entity.Id).ToImmutableArray();
                if (armed.IsEmpty) return false;
                if (!AttackPlace(armed, place, own: false, CnCNew.Simulation.Templates.WeaponSlot.Best, 0)) return false;
                Feedback(FeedbackKind.AttackMarker, world, place, EntityId.Invalid, Settings.MarkerFrames);
                return true;
            }
            case ContextAction.ResumeConstruction when target is not null:
            {
                var builder = ResumeBuilders(own, target).First();
                Orders.Clear(builder.Id);
                return Issue((sequence, tick) => new ResumeConstructCommand(Player, sequence, tick, builder.Id, target.Id)).Accepted;
            }
            case ContextAction.Dock when target is not null:
                return ForEach(own, unit => Issue((sequence, tick) => new DockCommand(Player, sequence, tick, unit.Id, target.Id)), world, target, FeedbackKind.EnterMarker);
            case ContextAction.GetRepaired when target is not null:
                return ForEach(RepairPadCandidates(world, own, target), unit => Issue((sequence, tick) => new DockCommand(Player, sequence, tick, unit.Id, target.Id)), world, target, FeedbackKind.EnterMarker);
            case ContextAction.Repair when target is not null:
                return ForEach(RepairBuilders(own, target), unit => Issue((sequence, tick) => new RepairCommand(Player, sequence, tick, unit.Id, target.Id)), world, target, FeedbackKind.EnterMarker);
            case ContextAction.Enter when target is not null:
                return ForEach(Entrants(world, own, target).ToList(), unit => Issue((sequence, tick) => new EnterContainerCommand(Player, sequence, tick, unit.Id, target.Id)), world, target, FeedbackKind.EnterMarker);
            case ContextAction.SetRallyPoint:
                return SetRally(world, own, ground);
            case ContextAction.Invalid:
                Feedback(FeedbackKind.InvalidOrder, world, ground, hint.Target, Settings.MarkerFrames);
                return false;
            default:
                return false;
        }
    }

    private bool ForEach(IEnumerable<Entity> units, Func<Entity, SubmitResult> order, SimulationWorld world, Entity target, FeedbackKind feedback)
    {
        var any = false;
        foreach (var unit in units)
        {
            Orders.Clear(unit.Id);
            any |= order(unit).Accepted;
        }
        if (any) Feedback(feedback, world, EntityPicker.CenterOf(target), target.Id, Settings.MarkerFrames);
        return any;
    }

    private bool SetRally(SimulationWorld world, IEnumerable<Entity> producers, Vector3 ground)
    {
        var (x, y) = (SimulationQueries.ToFixed(ground.X), SimulationQueries.ToFixed(ground.Y));
        var any = false;
        foreach (var producer in producers.Where(entity => SimulationQueries.ProductionOf(entity) is not null))
            any |= Issue((sequence, tick) => new SetRallyPointCommand(Player, sequence, tick, producer.Id, x, y)).Accepted;
        if (any) Feedback(FeedbackKind.RallyMarker, world, ground, EntityId.Invalid, Settings.MarkerFrames);
        return any;
    }

    /// <summary>
    /// <c>MSG_DO_MOVETO</c> to the selection: one unit gets a move order, several get one group order (<see cref="GroupMoveCommand"/>, <c>AIGroup::groupMoveToPosition</c>), in
    /// which the simulation gives every unit a destination of its own.
    /// </summary>
    private bool MoveTo(SimulationWorld world, IEnumerable<Entity> units, Vector3 ground)
    {
        var list = units.ToArray();
        foreach (var unit in list) Orders.Clear(unit.Id);
        var any = MoveGroup(world, list, ground, false);
        if (any) Feedback(FeedbackKind.MoveMarker, world, ground, EntityId.Invalid, Settings.MoveHintFrames);
        return any;
    }

    /// <summary>The move or attack-move order of <paramref name="units"/> to one point: a single command for one unit, a group order for several (units that cannot move take no part).</summary>
    private bool MoveGroup(SimulationWorld world, IReadOnlyList<Entity> units, Vector3 ground, bool attack)
    {
        var movable = units.Where(SimulationQueries.CanMove).ToArray();
        if (movable.Length == 0) return false;
        if (movable.Length == 1) return attack ? AttackMove(movable[0], ground) : Move(world, movable[0], ground);
        var (x, y) = (SimulationQueries.ToFixed(ground.X), SimulationQueries.ToFixed(ground.Y));
        ImmutableArray<EntityId> ids = [..movable.Select(unit => unit.Id)];
        return Issue((sequence, tick) => new GroupMoveCommand(Player, sequence, tick, ids, x, y, attack)).Accepted;
    }

    private bool Move(SimulationWorld world, Entity unit, Vector3 ground)
    {
        var (x, y) = (SimulationQueries.ToFixed(ground.X), SimulationQueries.ToFixed(ground.Y));
        return Issue((sequence, tick) => new SetMoveTargetCommand(Player, sequence, tick, unit.Id, x, y)).Accepted;
    }

    /// <summary><c>MSG_DO_ATTACKMOVETO</c>: the simulation's attack-move (the unit engages what it meets and marches on by itself).</summary>
    private bool AttackMove(Entity unit, Vector3 ground)
    {
        var (x, y) = (SimulationQueries.ToFixed(ground.X), SimulationQueries.ToFixed(ground.Y));
        return Issue((sequence, tick) => new AttackMoveCommand(Player, sequence, tick, unit.Id, x, y)).Accepted;
    }

    /// <summary><c>MSG_DO_STOP</c> (<c>groupIdle</c>): drops the standing orders and halts every mobile unit where it stands.</summary>
    public void Stop(SimulationWorld world, IEnumerable<Entity> units)
    {
        foreach (var unit in units)
        {
            Orders.Clear(unit.Id);
            if (SimulationQueries.CanMove(unit)) Issue((sequence, tick) => new SetMoveTargetCommand(Player, sequence, tick, unit.Id, unit.X, unit.Y));
        }
    }

    /// <summary>
    /// <c>MSG_DO_SCATTER</c> (<c>AIGroup::groupScatter</c>): every mobile unit moves four bounding radii away from the group centroid, farthest unit first,
    /// the centroid shifting 0.01 m west per unit so that a unit on the centroid still gets a direction.
    /// </summary>
    public void Scatter(SimulationWorld world, IEnumerable<Entity> units)
    {
        var movers = units.Where(entity => SimulationQueries.CanMove(entity) && !entity.Template.IsKindOf("IMMOBILE")).ToList();
        if (movers.Count == 0) return;
        var center = movers.Aggregate(Vector2.Zero, (sum, entity) => sum + SimulationQueries.GroundOf(entity)) / movers.Count;
        var ordered = movers.OrderByDescending(entity => Vector2.DistanceSquared(SimulationQueries.GroundOf(entity), center)).ThenBy(entity => entity.Id.Value).ToList();
        foreach (var unit in ordered)
        {
            center.X -= 0.01f;
            var position = SimulationQueries.GroundOf(unit);
            var delta = position - center;
            delta = delta == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(delta);
            var destination = position + delta * 4f * (float)unit.Template.BoundingCircleRadius.ToDecimal();
            Orders.Clear(unit.Id);
            Move(world, unit, new Vector3(destination, 0));
        }
    }

    // ------------------------------------------------------------------------------------------------ command buttons

    /// <summary>
    /// A control-bar button pressed for <paramref name="sources"/> (<c>processCommandUI</c>): immediate buttons issue their command, targeting
    /// buttons enter <see cref="CommandModeKind.ButtonTarget"/>, construction enters <see cref="CommandModeKind.Placement"/>.
    /// </summary>
    public ActivationResult Activate(SimulationWorld world, CommandButtonInfo button, ImmutableArray<Entity> sources)
    {
        ArgumentNullException.ThrowIfNull(button);
        switch (button.Kind)
        {
            case GuiCommandKind.UnitBuild:
            {
                var producer = sources.FirstOrDefault(entity => SimulationQueries.ProductionOf(entity) is not null);
                if (producer is null) return ActivationResult.NoSource;
                return Result(Issue((sequence, tick) => new QueueProductionCommand(Player, sequence, tick, producer.Id, button.Object)));
            }
            case GuiCommandKind.DozerConstruct:
            {
                var builder = sources.FirstOrDefault(entity => SimulationQueries.IsBuilder(entity) && SimulationQueries.AiOf(entity) is { IsBuilding: false });
                if (builder is null) return ActivationResult.NoSource;
                Cancel();
                Mode = CommandModeKind.Placement;
                _button = button;
                _builder = builder.Id;
                _sources = [builder.Id];
                _placementAngle = Settings.PlacementViewAngle?.Invoke(button.Object) ?? 0f;
                return ActivationResult.EnteredMode;
            }
            case GuiCommandKind.DozerConstructCancel:
                return ForAll(sources.Where(entity => entity.HasStatus(ObjectStatusBits.UnderConstruction)),
                    site => Issue((sequence, tick) => new CancelConstructCommand(Player, sequence, tick, site.Id)));
            case GuiCommandKind.PlayerUpgrade:
            {
                // The building whose button was pressed researches it (MSG_QUEUE_UPGRADE goes to the selected group).
                var producer = sources.IsEmpty ? (EntityId?)null : sources[0].Id;
                return Result(Issue((sequence, tick) => new PurchaseUpgradeCommand(Player, sequence, tick, button.Upgrade, producer)));
            }
            case GuiCommandKind.ObjectUpgrade:
            {
                if (sources.IsEmpty) return ActivationResult.NoSource;
                var target = sources[0];
                return Result(Issue((sequence, tick) => new PurchaseUpgradeCommand(Player, sequence, tick, button.Upgrade, target.Id)));
            }
            case GuiCommandKind.PurchaseScience:
            {
                var owned = world.PlayerSciences(Player);
                var science = button.Sciences.FirstOrDefault(name => world.Sciences.TryGet(name, out var info) && !owned.Has(info.Index));
                if (science is null) return ActivationResult.NoSource;
                return Result(Issue((sequence, tick) => new PurchaseScienceCommand(Player, sequence, tick, science)));
            }
            case GuiCommandKind.Sell:
                return ForAll(sources.Where(entity => entity.Template.IsKindOf("STRUCTURE")), target => Issue((sequence, tick) => new SellCommand(Player, sequence, tick, target.Id)));
            case GuiCommandKind.Stop:
                if (sources.IsEmpty) return ActivationResult.NoSource;
                Stop(world, sources);
                return ActivationResult.Issued;
            case GuiCommandKind.Evacuate:
            {
                var passengers = sources.Where(entity => entity.Container is not null).SelectMany(entity => entity.Container!.Contained).OrderBy(id => id.Value).ToList();
                if (passengers.Count == 0) return ActivationResult.NoSource;
                return ForAll(passengers, id => Issue((sequence, tick) => new ExitContainerCommand(Player, sequence, tick, id)));
            }
            case GuiCommandKind.SelectAllUnitsOfType:
                Selection.Replace(world.Entities.Where(entity => entity.Owner == Player && EntityPicker.IsMassSelectable(entity)
                                                                 && entity.Template.Name.Equals(button.Object, StringComparison.OrdinalIgnoreCase)).Select(entity => entity.Id));
                return ActivationResult.Issued;
            case GuiCommandKind.SpecialPower or GuiCommandKind.SpecialPowerFromShortcut:
            {
                var source = SpecialPowerSource(world, button, sources);
                if (source is null) return ActivationResult.NoSource;
                if (button.NeedsTarget) return EnterButtonMode(button, [source.Id]);
                return Result(Issue((sequence, tick) => new UseSpecialPowerCommand(Player, sequence, tick, source.Id, button.SpecialPower, SpecialPowerTargetKind.None, EntityId.Invalid, Fixed.Zero, Fixed.Zero, button.SpecialPowerOption)));
            }
            case GuiCommandKind.AttackMove or GuiCommandKind.Guard or GuiCommandKind.GuardWithoutPursuit or GuiCommandKind.GuardFlyingUnitsOnly:
            {
                var movers = sources.Where(SimulationQueries.CanMove).Select(entity => entity.Id).ToImmutableArray();
                if (movers.IsEmpty) return ActivationResult.NoSource;
                Cancel();
                Mode = button.Kind == GuiCommandKind.AttackMove ? CommandModeKind.AttackMove : CommandModeKind.Guard;
                _button = button;
                _sources = movers;
                return ActivationResult.EnteredMode;
            }
            case GuiCommandKind.SetRallyPoint:
            {
                var producers = sources.Where(entity => SimulationQueries.ProductionOf(entity) is not null).Select(entity => entity.Id).ToImmutableArray();
                return producers.IsEmpty ? ActivationResult.NoSource : EnterButtonMode(button, producers);
            }
            // USES_MINE_CLEARING_WEAPONSET switches to the mine-clearing weapon set first (MINE_CLEARING_DETAIL), which the simulation does not model: unsupported below.
            case GuiCommandKind.FireWeapon when !button.NeedsTarget && !button.Has(CommandOptions.UsesMineClearingWeaponset):
            {
                // MSG_DO_WEAPON: every member attacks its own position with the button's slot (a bomb truck's detonation, a convoy truck's nuke).
                var members = sources.Where(entity => SimulationQueries.CanTakeAttackOrder(world, entity)).Select(entity => entity.Id).ToImmutableArray();
                if (members.IsEmpty) return ActivationResult.NoSource;
                return AttackPlace(members, Vector3.Zero, own: true, button.WeaponSlotIndex, button.MaxShotsToFire) ? ActivationResult.Issued : ActivationResult.NoSource;
            }
            case GuiCommandKind.FireWeapon when !button.Has(CommandOptions.UsesMineClearingWeaponset)
                                                && (button.Has(CommandOptions.NeedTargetEnemyObject) || button.Has(CommandOptions.NeedTargetNeutralObject) || button.Has(CommandOptions.NeedTargetPos)):
            {
                var armed = sources.Where(entity => SimulationQueries.HasWeapon(entity)).Select(entity => entity.Id).ToImmutableArray();
                return armed.IsEmpty ? ActivationResult.NoSource : EnterButtonMode(button, armed);
            }
            default:
                UnsupportedCounts[button.Kind.ToString()] = UnsupportedCounts.GetValueOrDefault(button.Kind.ToString()) + 1;
                _feedback.Add(new CommandFeedback(FeedbackKind.Unsupported, Vector3.Zero, EntityId.Invalid, world.Tick, Settings.MarkerFrames, Detail: button.Kind.ToString()));
                return ActivationResult.Unsupported;
        }
    }

    /// <summary>
    /// The source of a special power: a selected object carrying it, or for <c>SPECIAL_POWER_FROM_SHORTCUT</c> the own object whose module is most
    /// ready (<c>findMostReadyShortcutSpecialPowerOfType</c>: ready first, then lowest id).
    /// </summary>
    public Entity? SpecialPowerSource(SimulationWorld world, CommandButtonInfo button, ImmutableArray<Entity> sources)
    {
        if (!world.SpecialPowers.TryGet(button.SpecialPower, out var power)) return null;
        bool Carries(Entity entity) => entity.Modules.OfType<ISpecialPowerModule>().Any(module => module.PowerIndex == power.Index);
        IEnumerable<Entity> pool = button.Kind == GuiCommandKind.SpecialPowerFromShortcut
            ? world.Entities.Where(entity => entity.Owner == Player && SimulationQueries.IsAlive(entity)).OrderBy(entity => entity.Id.Value)
            : sources;
        var carriers = pool.Where(Carries).ToList();
        return carriers.FirstOrDefault(entity => entity.Modules.OfType<ISpecialPowerModule>().Any(module => module.PowerIndex == power.Index && module.IsReady(world)))
               ?? carriers.FirstOrDefault();
    }

    private ActivationResult EnterButtonMode(CommandButtonInfo button, ImmutableArray<EntityId> sources)
    {
        Cancel();
        Mode = CommandModeKind.ButtonTarget;
        _button = button;
        _sources = sources;
        return ActivationResult.EnteredMode;
    }

    private static ActivationResult Result(SubmitResult result) => result.Accepted ? ActivationResult.Issued : ActivationResult.Rejected;

    private static ActivationResult ForAll<T>(IEnumerable<T> items, Func<T, SubmitResult> issue)
    {
        var any = false;
        var accepted = false;
        foreach (var item in items)
        {
            any = true;
            accepted |= issue(item).Accepted;
        }
        return !any ? ActivationResult.NoSource : accepted ? ActivationResult.Issued : ActivationResult.Rejected;
    }

    /// <summary>
    /// The target check of <c>GUICommandTranslator</c> / <c>issueSpecialPowerCommand</c>: an object matching the button's relationship options wins
    /// over a position; a position needs <c>NEED_TARGET_POS</c>.
    /// </summary>
    private bool ResolveButtonTarget(SimulationWorld world, Entity? target, Vector3? ground, out Entity? objectTarget, out Vector3? position)
    {
        objectTarget = null;
        position = null;
        var button = _button;
        if (button is null) return false;
        if (target is not null && (button.Options & CommandOptions.NeedObjectTarget) != 0)
        {
            var relation = target.Owner == Player ? Relationship.Allies : world.RelationshipOf(Player, target.Owner);
            var accepted = relation switch
            {
                Relationship.Enemies => button.Has(CommandOptions.NeedTargetEnemyObject),
                Relationship.Neutral => button.Has(CommandOptions.NeedTargetNeutralObject),
                _ => button.Has(CommandOptions.NeedTargetAllyObject),
            };
            if (accepted)
            {
                objectTarget = target;
                return true;
            }
        }
        if (ground is not null && button.Has(CommandOptions.NeedTargetPos))
        {
            position = ground;
            return true;
        }
        return false;
    }

    /// <summary>The click that completes <see cref="CommandModeKind.AttackMove"/>, <see cref="CommandModeKind.Guard"/> or <see cref="CommandModeKind.ButtonTarget"/>.</summary>
    public bool CompleteTarget(SimulationWorld world, IViewProjection view, Vector2 screen)
    {
        var target = Selection.Picker.Pick(world, Player, view, screen);
        Vector3? ground = view.TryScreenToGround(screen, out var point) ? point : null;
        return CompleteTarget(world, target, ground);
    }

    public bool CompleteTarget(SimulationWorld world, Entity? target, Vector3? ground)
    {
        var sources = _sources.Select(world.FindEntity).OfType<Entity>().Where(entity => entity.Owner == Player && SimulationQueries.IsAlive(entity)).ToImmutableArray();
        switch (Mode)
        {
            case CommandModeKind.AttackMove or CommandModeKind.Guard:
            {
                if (ground is not { } point) { Feedback(FeedbackKind.InvalidOrder, world, Vector3.Zero, EntityId.Invalid, Settings.MarkerFrames); return false; }
                var any = false;
                foreach (var unit in sources) Orders.Clear(unit.Id);
                if (Mode == CommandModeKind.AttackMove)
                {
                    // MSG_DO_ATTACKMOVETO to the selection: one group order, as for a plain move.
                    any = MoveGroup(world, sources, point, true);
                    if (any)
                        foreach (var unit in sources.Where(SimulationQueries.CanMove))
                            Orders.Set(unit.Id, new StandingOrder(StandingOrderKind.AttackMove, EntityId.Invalid, new Vector2(point.X, point.Y), [], world.Tick));
                }
                else
                {
                    // MSG_DO_GUARD_POSITION to the selection (groupGuardPosition): the simulation's guard, in the mode of the button (F-58).
                    var guards = sources.Where(SimulationQueries.CanMove).Select(unit => unit.Id).ToImmutableArray();
                    var mode = _button?.Kind switch
                    {
                        GuiCommandKind.GuardWithoutPursuit => GuardMode.WithoutPursuit,
                        GuiCommandKind.GuardFlyingUnitsOnly => GuardMode.FlyingUnitsOnly,
                        _ => GuardMode.Normal,
                    };
                    var (x, y) = (SimulationQueries.ToFixed(point.X), SimulationQueries.ToFixed(point.Y));
                    any = !guards.IsEmpty && Issue((sequence, tick) => new GuardCommand(Player, sequence, tick, guards, x, y, EntityId.Invalid, mode)).Accepted;
                }
                if (any) Feedback(FeedbackKind.MoveMarker, world, point, EntityId.Invalid, Settings.MoveHintFrames);
                Cancel();
                return any;
            }
            case CommandModeKind.ButtonTarget:
            {
                var button = _button!;
                if (!ResolveButtonTarget(world, target, ground, out var objectTarget, out var position))
                {
                    Feedback(FeedbackKind.InvalidOrder, world, ground ?? Vector3.Zero, target?.Id ?? EntityId.Invalid, Settings.MarkerFrames);
                    return false;
                }
                var issued = button.Kind switch
                {
                    GuiCommandKind.SetRallyPoint when position is { } rally => SetRally(world, sources, rally),
                    GuiCommandKind.SpecialPower or GuiCommandKind.SpecialPowerFromShortcut => UseSpecialPower(world, button, sources, objectTarget, position),
                    // ATTACK_OBJECTS_POSITION: the weapon goes to the object's place (MSG_DO_WEAPON_AT_LOCATION).
                    GuiCommandKind.FireWeapon when objectTarget is not null && button.Has(CommandOptions.AttackObjectsPosition)
                        => AttackPlace(sources, EntityPicker.CenterOf(objectTarget), own: false, button.WeaponSlotIndex, button.MaxShotsToFire),
                    GuiCommandKind.FireWeapon when objectTarget is not null => OrderAttack(world, sources, objectTarget),
                    // MSG_DO_WEAPON_AT_LOCATION: the button's slot at the place for its shots.
                    GuiCommandKind.FireWeapon when position is { } place => AttackPlace(sources, place, own: false, button.WeaponSlotIndex, button.MaxShotsToFire),
                    _ => Unsupported(world, $"{button.Kind}Target", position ?? Vector3.Zero),
                };
                Cancel();
                return issued;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// An attack on a place to the group (<see cref="AttackPositionCommand"/>, F-64): the client's standing orders of the units end - the attack is the simulation's.
    /// </summary>
    private bool AttackPlace(ImmutableArray<EntityId> units, Vector3 place, bool own, int slot, int shots)
    {
        foreach (var unit in units) Orders.Clear(unit);
        return Issue((sequence, tick) => new AttackPositionCommand(Player, sequence, tick, units, SimulationQueries.ToFixed(place.X), SimulationQueries.ToFixed(place.Y), own, slot, shots)).Accepted;
    }

    private bool AttackPlace(ImmutableArray<Entity> units, Vector3 place, bool own, int slot, int shots)
        => !units.IsEmpty && AttackPlace(units.Select(unit => unit.Id).ToImmutableArray(), place, own, slot, shots);

    private bool OrderAttack(SimulationWorld world, ImmutableArray<Entity> units, Entity target)
    {
        foreach (var unit in units)
            Orders.Set(unit.Id, new StandingOrder(StandingOrderKind.Attack, target.Id, SimulationQueries.GroundOf(target), [], world.Tick));
        if (!units.IsEmpty) Feedback(FeedbackKind.AttackMarker, world, EntityPicker.CenterOf(target), target.Id, Settings.MarkerFrames);
        return !units.IsEmpty;
    }

    private bool UseSpecialPower(SimulationWorld world, CommandButtonInfo button, ImmutableArray<Entity> sources, Entity? objectTarget, Vector3? position)
    {
        if (sources.IsEmpty) return false;
        var source = sources[0];
        SubmitResult result;
        if (objectTarget is not null)
            result = Issue((sequence, tick) => new UseSpecialPowerCommand(Player, sequence, tick, source.Id, button.SpecialPower, SpecialPowerTargetKind.Object, objectTarget.Id, Fixed.Zero, Fixed.Zero, button.SpecialPowerOption));
        else
        {
            var (x, y) = (SimulationQueries.ToFixed(position!.Value.X), SimulationQueries.ToFixed(position.Value.Y));
            result = Issue((sequence, tick) => new UseSpecialPowerCommand(Player, sequence, tick, source.Id, button.SpecialPower, SpecialPowerTargetKind.Location, EntityId.Invalid, x, y, button.SpecialPowerOption));
        }
        if (result.Accepted) Feedback(FeedbackKind.AttackMarker, world, objectTarget is null ? position!.Value : EntityPicker.CenterOf(objectTarget), objectTarget?.Id ?? EntityId.Invalid, Settings.MarkerFrames);
        return result.Accepted;
    }

    // ------------------------------------------------------------------------------------------------ placement

    /// <summary>Follows the cursor with the ghost; while anchored and dragged past the threshold, the drag direction sets the angle.</summary>
    public void UpdateGhost(SimulationWorld world, IViewProjection view, Vector2 screen)
    {
        if (Mode != CommandModeKind.Placement || _button is null) return;
        var hasGround = view.TryScreenToGround(screen, out var point);
        if (_anchor is { } anchor)
        {
            if (hasGround && (_rotating || Vector2.Distance(screen, _anchorScreen) >= Settings.PlacementDragPixels))
            {
                _rotating = true;
                var direction = new Vector2(point.X - anchor.X, point.Y - anchor.Y);
                if (direction != Vector2.Zero) _placementAngle = MathF.Atan2(direction.Y, direction.X);
            }
            Ghost = MakeGhost(world, anchor, anchored: true);
            return;
        }
        Ghost = hasGround ? MakeGhost(world, point, anchored: false) : new PlacementGhost(_button.Object, Vector3.Zero, _placementAngle, false, PlacementIssue.NoGround, false);
    }

    private PlacementGhost MakeGhost(SimulationWorld world, Vector3 position, bool anchored)
    {
        var issue = SimulationQueries.CheckPlacement(world, Player, world.FindEntity(_builder), _button!.Object,
            SimulationQueries.ToFixed(position.X), SimulationQueries.ToFixed(position.Y), SimulationQueries.ToFixed(_placementAngle));
        return new PlacementGhost(_button.Object, position, _placementAngle, issue == PlacementIssue.None, issue, anchored);
    }

    /// <summary>Left button down in placement mode: anchors the ghost (<c>setPlacementStart</c>); off the terrain cancels the placement.</summary>
    public void PlacementPress(SimulationWorld world, IViewProjection view, Vector2 screen)
    {
        if (Mode != CommandModeKind.Placement) return;
        if (!view.TryScreenToGround(screen, out var point) || world.FindEntity(_builder) is null)
        {
            Cancel();
            return;
        }
        _anchor = point;
        _anchorScreen = screen;
        _rotating = false;
        Ghost = MakeGhost(world, point, anchored: true);
    }

    /// <summary>Left button up: places the structure when legal (<c>MSG_DOZER_CONSTRUCT</c>), otherwise reports why and lets the player retry.</summary>
    public bool PlacementRelease(SimulationWorld world, IViewProjection view, Vector2 screen)
    {
        if (Mode != CommandModeKind.Placement || _anchor is not { } anchor) return false;
        UpdateGhost(world, view, screen);
        var ghost = Ghost!;
        if (!ghost.IsValid)
        {
            _feedback.Add(new CommandFeedback(FeedbackKind.InvalidOrder, anchor, EntityId.Invalid, world.Tick, Settings.MarkerFrames, Detail: ghost.Reason.ToString()));
            _anchor = null;
            _rotating = false;
            return false;
        }
        var (builder, template) = (_builder, _button!.Object);
        var (x, y, angle) = (SimulationQueries.ToFixed(anchor.X), SimulationQueries.ToFixed(anchor.Y), SimulationQueries.ToFixed(_placementAngle));
        Orders.Clear(builder);
        var result = Issue((sequence, tick) => new ConstructCommand(Player, sequence, tick, builder, template, x, y, angle));
        Cancel();
        return result.Accepted;
    }

    /// <summary>A click on a production queue icon (<c>Command_CancelUnitCreate</c>): cancels that entry with a full refund.</summary>
    public bool CancelProduction(EntityId producer, int index)
        => Issue((sequence, tick) => new CancelProductionCommand(Player, sequence, tick, producer, index)).Accepted;

    /// <summary>The cancel button of a structure under construction (<c>MSG_DOZER_CANCEL_CONSTRUCT</c>).</summary>
    public bool CancelConstruction(EntityId site) => Issue((sequence, tick) => new CancelConstructCommand(Player, sequence, tick, site)).Accepted;

    // ------------------------------------------------------------------------------------------------ ticking and plumbing

    /// <summary>Once per logic tick, before the world steps: prunes the selection and advances the standing orders.</summary>
    public void Update(SimulationWorld world)
    {
        Orders.Update(world, Player, Issue, _sink.DelayTicks);
        if (Mode == CommandModeKind.Placement && world.FindEntity(_builder) is not { } builder || Mode == CommandModeKind.Placement && !SimulationQueries.IsAlive(world.FindEntity(_builder)!))
            Cancel();
    }

    /// <summary>Orders that were refused only when they were executed (their pre-check had passed), and orders that passed although their pre-check had refused them.</summary>
    public int LateRejections { get; private set; }
    public int LateAcceptances { get; private set; }

    /// <summary>
    /// The final answer to an order of this player (<see cref="CommandPath.Resolved"/>). The display followed the pre-check when the order was given; only an answer that
    /// differs from it changes anything: an order refused after all gets the feedback it would have got at once.
    /// </summary>
    public void OnResolved(CommandResolution resolution)
    {
        if (resolution.Player != Player || !resolution.Deviates) return;
        if (resolution.Result.Accepted)
        {
            LateAcceptances++;
            return;
        }
        LateRejections++;
        _feedback.Add(new CommandFeedback(FeedbackKind.Rejected, Vector3.Zero, EntityId.Invalid, resolution.Command.Tick, Settings.MarkerFrames, resolution.Result.Reason, resolution.Command.GetType().Name));
    }

    private SubmitResult Issue(Func<ulong, long, SimCommand> make)
    {
        SimCommand? made = null;
        var result = _sink.Issue((sequence, tick) => made = make(sequence, tick));
        if (!result.Accepted)
            _feedback.Add(new CommandFeedback(FeedbackKind.Rejected, Vector3.Zero, EntityId.Invalid, made?.Tick ?? 0, Settings.MarkerFrames, result.Reason, made?.GetType().Name ?? string.Empty));
        return result;
    }

    private bool Unsupported(SimulationWorld world, string what, Vector3 position)
    {
        UnsupportedCounts[what] = UnsupportedCounts.GetValueOrDefault(what) + 1;
        _feedback.Add(new CommandFeedback(FeedbackKind.Unsupported, position, EntityId.Invalid, world.Tick, Settings.MarkerFrames, Detail: what));
        return false;
    }

    private void Feedback(FeedbackKind kind, SimulationWorld world, Vector3 position, EntityId target, int frames)
        => _feedback.Add(new CommandFeedback(kind, position, target, world.Tick, frames));
}

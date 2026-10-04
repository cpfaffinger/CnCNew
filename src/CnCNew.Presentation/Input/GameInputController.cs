using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

public enum MouseScheme
{
    /// <summary>The retail default (<c>GlobalData::m_useAlternateMouse = FALSE</c>): the left button selects and orders, the right button deselects and cancels.</summary>
    LeftClickCommands,
    /// <summary>The "alternate mouse" option: the left button selects, the right button orders and cancels.</summary>
    RightClickCommands,
}

public sealed record GameInputSettings(
    MouseScheme Scheme = MouseScheme.LeftClickCommands,
    InputSettings? Input = null,
    SelectionSettings? Selection = null,
    CameraSettings? Camera = null,
    CommandSettings? Commands = null);

/// <summary>
/// The input pipeline in the order of the original message translators (<c>GameClient.cpp</c>: meta keys 20, control-bar hotkeys 25, placement 30,
/// GUI command targeting 40, selection 50, camera 60, context commands 70). The client feeds raw <see cref="InputEvent"/>s and calls
/// <see cref="Tick"/> once per logic tick before stepping the world; everything the player orders leaves through the <see cref="ICommandSink"/>.
/// </summary>
public sealed class GameInputController
{
    private readonly Dictionary<KeyCode, KeyModifiers> _keyDownModifiers = [];
    private readonly HashSet<KeyCode> _metaConsumed = [];
    private readonly HashSet<KeyModifiers> _modifierCombos = [];

    public GameInputController(PlayerId localPlayer, ICommandSink sink, HotkeyMap hotkeys, CommandButtonCatalog? buttons = null,
        Func<string, string?>? text = null, GameInputSettings? settings = null)
    {
        Settings = settings ?? new GameInputSettings();
        State = new InputState(Settings.Input);
        Hotkeys = hotkeys;
        Selection = new SelectionModel(localPlayer, Settings.Selection);
        Commands = new CommandModeController(localPlayer, sink, Selection, Settings.Commands);
        Camera = new CameraInputController(Settings.Camera);
        CommandBar = buttons is null ? null : new CommandBarModel(buttons, text);
        Selection.SelectionChanged += _ => Commands.Cancel();
    }

    public GameInputSettings Settings { get; }
    public InputState State { get; }
    public HotkeyMap Hotkeys { get; set; }
    public SelectionModel Selection { get; }
    public CommandModeController Commands { get; }
    public CameraInputController Camera { get; }
    public CommandBarModel? CommandBar { get; }

    /// <summary>Metas for the UI layer (options menu, pause, chat, diplomacy, screenshots, control bar toggle, ...), in the order they fired.</summary>
    public event Action<MetaAction>? UiRequest;

    /// <summary>The context hint under the cursor after the last event.</summary>
    public ContextHint Hint { get; private set; }

    public void Process(InputEvent inputEvent, SimulationWorld world, IViewProjection view)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        var wasPlacing = Commands.Mode == CommandModeKind.Placement;
        var gestures = State.Apply(inputEvent);
        switch (inputEvent)
        {
            case KeyEvent key:
                OnKey(key, world, view);
                break;
            case MouseWheelEvent wheel:
                Camera.OnWheel(wheel.Notches);
                break;
            case MouseButtonEvent { Button: MouseButton.Left, IsDown: true } press when Commands.Mode == CommandModeKind.Placement:
                Commands.PlacementPress(world, view, press.Position);
                break;
            case FocusLostEvent:
                Commands.ReleaseModifiers();
                Camera.OnFocusLost();
                _keyDownModifiers.Clear();
                _metaConsumed.Clear();
                _modifierCombos.Clear();
                break;
        }
        foreach (var gesture in gestures) OnGesture(gesture, world, view, wasPlacing);
        if (State.HasMouse) Hint = Commands.Hover(world, view, State.MousePosition);
    }

    /// <summary>Once per logic tick, before the world steps.</summary>
    public void Tick(SimulationWorld world)
    {
        Selection.Prune(world);
        Commands.Update(world);
    }

    public CameraIntent UpdateCamera(IViewProjection view, float deltaSeconds)
    {
        var intent = Camera.Update(State, Hotkeys, view.ViewportSize, deltaSeconds);
        // The intent's y is "forward" (up the screen); the cursor direction is a screen offset with y down, like InGameUI::getScrollAmount.
        ScrollOffset = new Vector2(intent.Scroll.X, -intent.Scroll.Y);
        IsScrolling = intent.Scroll != Vector2.Zero || (Camera.Settings.RightDragScrolls && State.IsDragging(MouseButton.Right));
        return intent;
    }

    /// <summary>The view scrolled in the last <see cref="UpdateCamera"/> (keys, screen edge or right drag): <c>InGameUI::isScrolling</c>.</summary>
    public bool IsScrolling { get; private set; }

    /// <summary>The scroll of the last <see cref="UpdateCamera"/> as a screen offset (x right, y down); only its direction matters.</summary>
    public Vector2 ScrollOffset { get; private set; }

    /// <summary>
    /// Re-evaluates what is under the resting pointer; the world moves under it (a unit walks in, a target dies) and the selection can change
    /// without any input event. Issues nothing.
    /// </summary>
    public void RefreshHint(SimulationWorld world, IViewProjection view)
    {
        if (State.HasMouse) Hint = Commands.Hover(world, view, State.MousePosition);
    }

    /// <summary>
    /// The cursor to show (<c>InGameUI::setScrolling</c>, <c>InGameUI::evaluateContextCommand</c>'s cursor switch): the directional scroll cursor
    /// while the view scrolls, the arrow over a window, otherwise the cursor of the pending command mode or of the context under the pointer.
    /// Modifier keys act through <see cref="Process"/>, which re-evaluates the hint on every event, so the cursor follows them without a mouse move.
    /// </summary>
    public CursorState Cursor(bool pointerOverWindow)
    {
        if (IsScrolling) return new CursorState(CursorKind.Scroll, ScrollOffset);
        if (pointerOverWindow || !State.HasMouse) return CursorState.Arrow;
        return new CursorState(Hint.Cursor, Vector2.Zero);
    }

    public CommandBarView? BuildCommandBar(SimulationWorld world) => CommandBar?.Build(world, Selection);

    // ------------------------------------------------------------------------------------------------ keyboard

    private void OnKey(KeyEvent key, SimulationWorld world, IViewProjection view)
    {
        if (key.IsRepeat) return;
        var modifiers = State.Modifiers;
        if (KeyNames.IsModifier(key.Key))
        {
            if (key.IsDown)
            {
                // A modifier alone is the KEY_NONE binding (BEGIN_FORCEATTACK = KEY_NONE + CTRL).
                _modifierCombos.Add(modifiers);
                Fire(new KeyBinding(KeyCode.None, modifiers, KeyTransition.Down), world, view);
                return;
            }
            // onKeyModStateRemoved: every held combination that is no longer covered fires its UP binding, whatever the release order.
            foreach (var combo in _modifierCombos.Where(combo => (combo & modifiers) != combo).OrderBy(combo => combo).ToList())
            {
                _modifierCombos.Remove(combo);
                Fire(new KeyBinding(KeyCode.None, combo, KeyTransition.Up), world, view);
            }
            foreach (var (held, heldModifiers) in _keyDownModifiers.Where(pair => (pair.Value & modifiers) != pair.Value).OrderBy(pair => pair.Key).ToList())
            {
                _keyDownModifiers[held] = heldModifiers & modifiers;
                Fire(new KeyBinding(held, heldModifiers, KeyTransition.Up), world, view);
            }
            return;
        }
        if (key.IsDown)
        {
            _keyDownModifiers[key.Key] = modifiers;
            if (Fire(new KeyBinding(key.Key, modifiers, KeyTransition.Down), world, view)) _metaConsumed.Add(key.Key);
            else _metaConsumed.Remove(key.Key);
            return;
        }
        var downModifiers = _keyDownModifiers.Remove(key.Key, out var recorded) ? recorded : modifiers;
        var consumed = Fire(new KeyBinding(key.Key, downModifiers, KeyTransition.Up), world, view) | _metaConsumed.Remove(key.Key);
        // HotKeyTranslator acts on the key-up without modifiers. A key whose press already fired a meta is skipped, so S does not stop twice
        // (the original lets both the STOP meta and a Stop button hotkey fire).
        if (!consumed && modifiers == KeyModifiers.None && KeyNames.CharacterOf(key.Key) is { } character && CommandBar is { } bar)
            bar.ActivateHotkey(world, bar.Build(world, Selection), character, Commands, out _);
    }

    private bool Fire(KeyBinding binding, SimulationWorld world, IViewProjection view)
    {
        var actions = Hotkeys.Resolve(binding, HotkeyContexts.Game);
        foreach (var action in actions) DispatchMeta(action, world, view);
        return !actions.IsEmpty;
    }

    /// <summary>Executes one meta action (<c>CommandXlat</c>/<c>SelectionXlat</c>/<c>LookAtXlat</c> meta handlers).</summary>
    public void DispatchMeta(MetaAction action, SimulationWorld world, IViewProjection view)
    {
        if (Camera.OnMeta(action) || Commands.OnMeta(world, action)) return;
        if (MetaActionNames.IsTeamAction(action, out var kind, out var group))
        {
            if (Selection.TeamAction(world, kind, group, State.LastTime) is { } center) Camera.RequestCenter(center);
            return;
        }
        switch (action)
        {
            case MetaAction.SelectAll: Selection.SelectAll(world, view, aircraftOnly: false); return;
            case MetaAction.SelectAllAircraft: Selection.SelectAll(world, view, aircraftOnly: true); return;
            case MetaAction.SelectMatchingUnits: Selection.SelectMatchingUnits(world, view); return;
            case MetaAction.SelectNextUnit or MetaAction.SelectPrevUnit or MetaAction.SelectNextWorker or MetaAction.SelectPrevWorker:
            {
                var workers = action is MetaAction.SelectNextWorker or MetaAction.SelectPrevWorker;
                var forward = action is MetaAction.SelectNextUnit or MetaAction.SelectNextWorker;
                if (Selection.SelectNext(world, workers, forward) is { } center) Camera.RequestCenter(center);
                return;
            }
            case MetaAction.ViewCommandCenter:
            {
                var center = world.Entities.Where(entity => entity.Owner == Selection.LocalPlayer && entity.Template.IsKindOf("COMMANDCENTER") && SimulationQueries.IsAlive(entity))
                    .OrderBy(entity => entity.Id.Value).FirstOrDefault();
                if (center is not null) Camera.RequestCenter(SimulationQueries.GroundOf(center));
                return;
            }
            case MetaAction.Options when Commands.Mode != CommandModeKind.None:
                // Deviation: Escape first leaves a targeting or placement mode; the original has no key cancel and opens the menu at once.
                Commands.Cancel();
                return;
            default:
                UiRequest?.Invoke(action);
                return;
        }
    }

    // ------------------------------------------------------------------------------------------------ mouse

    private void OnGesture(PointerGesture gesture, SimulationWorld world, IViewProjection view, bool wasPlacing)
    {
        if (gesture.Button == MouseButton.Middle)
        {
            Camera.OnGesture(gesture);
            return;
        }
        if (gesture.Button == MouseButton.Right)
        {
            if (gesture.Kind is GestureKind.Click or GestureKind.DoubleClick) OnRightClick(gesture, world, view);
            return;
        }
        if (Commands.Mode == CommandModeKind.Placement || wasPlacing)
        {
            switch (gesture.Kind)
            {
                case GestureKind.DragStarted or GestureKind.DragMoved:
                    Commands.UpdateGhost(world, view, gesture.Position);
                    break;
                case GestureKind.Click or GestureKind.DoubleClick or GestureKind.DragEnded:
                    Commands.PlacementRelease(world, view, gesture.Position);
                    break;
            }
            return;
        }
        if (Commands.Mode is CommandModeKind.AttackMove or CommandModeKind.Guard or CommandModeKind.ButtonTarget)
        {
            // GUICommandTranslator swallows the button-down, so no box starts; the click completes the command.
            if (gesture.Kind is GestureKind.Click or GestureKind.DoubleClick) Commands.CompleteTarget(world, view, gesture.Position);
            return;
        }
        switch (gesture.Kind)
        {
            case GestureKind.DragEnded:
                Selection.BoxSelect(world, view, gesture.Rect, Commands.PreferSelection);
                break;
            case GestureKind.DoubleClick when (gesture.Modifiers & KeyModifiers.Control) == 0
                                              && Selection.DoubleClickSelect(world, view, gesture.Position, (gesture.Modifiers & KeyModifiers.Alt) != 0, Commands.PreferSelection):
                break;
            case GestureKind.Click or GestureKind.DoubleClick:
                OnLeftClick(gesture.Position, world, view);
                break;
        }
    }

    private void OnLeftClick(Vector2 screen, SimulationWorld world, IViewProjection view)
    {
        var target = Selection.Picker.Pick(world, Selection.LocalPlayer, view, screen);
        if (Settings.Scheme == MouseScheme.RightClickCommands)
        {
            // Alternate mouse: a left click never orders; on empty ground it deselects everything.
            if (target is null || !Selection.SelectFromCandidates(world, [target], isPoint: true, Commands.PreferSelection)) Selection.Clear();
            return;
        }
        Vector3? ground = view.TryScreenToGround(screen, out var point) ? point : null;
        var hint = Commands.Evaluate(world, target, ground);
        // contextCommandForNewSelection: a valid order on the clicked object wins over selecting it, unless Shift prefers selection.
        if (target is not null && !(hint.IsOrder && !Commands.PreferSelection))
        {
            if (Selection.SelectFromCandidates(world, [target], isPoint: true, Commands.PreferSelection)) return;
        }
        if (hint.IsOrder || hint.Action == ContextAction.Invalid) Commands.IssueContext(world, hint, target);
    }

    private void OnRightClick(PointerGesture gesture, SimulationWorld world, IViewProjection view)
    {
        if (Commands.Mode != CommandModeKind.None)
        {
            Commands.Cancel();
            return;
        }
        if (Settings.Scheme == MouseScheme.LeftClickCommands)
        {
            Selection.Clear();
            return;
        }
        var target = Selection.Picker.Pick(world, Selection.LocalPlayer, view, gesture.Position);
        Vector3? ground = view.TryScreenToGround(gesture.Position, out var point) ? point : null;
        var hint = Commands.Evaluate(world, target, ground);
        if (hint.IsOrder || hint.Action == ContextAction.Invalid) Commands.IssueContext(world, hint, target);
    }

    /// <summary>The ids currently in control group <paramref name="index"/> (for the UI's group badges).</summary>
    public ImmutableArray<EntityId> Group(int index) => Selection.Group(index);
}

using System.Globalization;
using System.Numerics;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.World;

namespace CnCNew.Client.Stride.Testing;

/// <summary>
/// A test facility of the client (<c>--input-script FILE</c>, together with <c>--smoke-test --frames N</c>): replays pointer and key input at the place where the device input enters
/// <see cref="ClientRuntime"/>, so a scripted "select, press a button, click the ground" runs through the real client path - the HUD laid out for the real back buffer with the renderer's
/// text measure, one update and one draw list per frame - instead of through a headless controller. It never writes to the simulation: everything it does is an
/// <see cref="InputEvent"/>, except <c>look</c>, which only moves the camera.
/// <para>
/// File format, one step per line, <c>#</c> starts a comment: <c>FRAME VERB ARGUMENTS</c>. Frames count the runtime's updates from 1. Verbs: <c>move TARGET</c>,
/// <c>down [left|right|middle] [TARGET]</c>, <c>up [left|right|middle] [TARGET]</c>, <c>click [left|right|middle] TARGET</c> (move and press in that frame, release two frames later on
/// the same pixel), <c>key down|up KEYCODE</c>, <c>look TARGET</c>. Targets are resolved in the frame they are used: <c>screen:X,Y</c>, <c>own:KINDOF[#N]</c> and
/// <c>template:NAME[#N]</c> (the N-th own living object by id, at its picking centre), <c>button:WINDOW</c> (centre of a HUD window, e.g. <c>ControlBar.wnd:ButtonOptions</c>),
/// <c>command:KIND[:OBJECT]</c> (the first enabled command bar slot of that <see cref="GuiCommandKind"/>), <c>ground:X,Y</c>, <c>ground:+DX,+DY from ENTITY</c> and
/// <c>placement from ENTITY</c> (the first spot around that object the pending construction would be accepted on).
/// </para>
/// <para>
/// As with the real device (<see cref="CnCNew.Client.Stride.Ui.StrideInputTranslator"/>) there is one pointer position per frame: a move event first when it changed, then the frame's
/// button events carrying that position. A target that cannot be resolved is reported and counted, never skipped silently; the process then exits with code 3.
/// </para>
/// </summary>
internal sealed class InputScript
{
    private static readonly Vector2[] PlacementOffsets =
    [
        new(150, 0), new(-150, 0), new(0, 150), new(0, -150), new(110, 110), new(-110, -110), new(110, -110), new(-110, 110),
        new(250, 0), new(-250, 0), new(0, 250), new(0, -250), new(200, 200), new(-200, -200),
    ];

    private readonly List<Step> _steps;
    private readonly List<string> _log = [];
    private readonly Dictionary<uint, string> _sites = [];
    private readonly HashSet<uint> _completedSites = [];
    private LiveGame? _game;
    private MatchController? _hud;
    private Vector2 _pointer = new(float.NaN, float.NaN);
    private Vector2 _lastSent = new(float.NaN, float.NaN);
    private CommandModeKind _mode;
    private string _ghost = "none";
    private int _frame;
    private int _events;
    private int _unresolved;
    private int _accepted;
    private int _rejected;
    private int _invalidOrders;
    private int _selectionChanges;

    private InputScript(List<Step> steps) => _steps = steps;

    private sealed record Step(int Frame, int Line, string Verb, string Arguments);

    public int Unresolved => _unresolved;

    public static InputScript Load(string path)
    {
        var steps = new List<Step>();
        var lines = File.ReadAllLines(path);
        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index];
            var comment = text.IndexOf('#');
            // '#' also numbers objects (own:DOZER#2): only a '#' at the start or after white space starts a comment.
            while (comment > 0 && !char.IsWhiteSpace(text[comment - 1])) comment = text.IndexOf('#', comment + 1);
            if (comment >= 0) text = text[..comment];
            text = text.Trim();
            if (text.Length == 0) continue;
            var parts = text.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var frame) || frame < 1)
                throw new InvalidDataException($"{path}({index + 1}): expected 'FRAME VERB ARGUMENTS' with FRAME >= 1.");
            var verb = parts[1].ToLowerInvariant();
            var arguments = parts.Length > 2 ? parts[2].Trim() : string.Empty;
            switch (verb)
            {
                case "click":
                {
                    var (button, target) = SplitButton(arguments);
                    if (target.Length == 0) throw new InvalidDataException($"{path}({index + 1}): click needs a target.");
                    steps.Add(new Step(frame, index + 1, "move", target));
                    steps.Add(new Step(frame, index + 1, "down", button));
                    steps.Add(new Step(frame + 2, index + 1, "up", button));
                    break;
                }
                case "move" or "look" when arguments.Length > 0:
                case "down" or "up":
                    steps.Add(new Step(frame, index + 1, verb, arguments));
                    break;
                case "key":
                {
                    var key = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (key.Length != 2 || key[0] is not ("down" or "up") || !Enum.TryParse<KeyCode>(key[1], true, out _))
                        throw new InvalidDataException($"{path}({index + 1}): expected 'key down|up KEYCODE'.");
                    steps.Add(new Step(frame, index + 1, verb, arguments));
                    break;
                }
                default:
                    throw new InvalidDataException($"{path}({index + 1}): unknown or incomplete step '{text}'.");
            }
        }
        // Stable: steps of one frame keep their file order.
        return new InputScript([..steps.Select((step, order) => (step, order)).OrderBy(item => item.step.Frame).ThenBy(item => item.order).Select(item => item.step)]);
    }

    private static (string Button, string Target) SplitButton(string arguments)
    {
        var parts = arguments.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0].ToLowerInvariant() is "left" or "right" or "middle")
            return (parts[0].ToLowerInvariant(), parts.Length > 1 ? parts[1].Trim() : string.Empty);
        return ("left", arguments);
    }

    /// <summary>Connects the report to the running game: selection changes, submitted orders with the simulation's answer, refused clicks, construction sites.</summary>
    public void Attach(LiveGame game, MatchController hud)
    {
        _game = game;
        _hud = hud;
        hud.Input.Selection.SelectionChanged += selected =>
        {
            _selectionChanges++;
            Log($"selection [{string.Join(", ", selected.Select(id => game.World.FindEntity(id) is { } entity ? $"{entity.Template.Name}#{id.Value}" : $"#{id.Value}"))}]");
        };
        game.CommandIssued += (command, result) =>
        {
            if (result.Accepted) _accepted++;
            else _rejected++;
            Log($"command {command.GetType().Name} {(result.Accepted ? "accepted" : $"rejected ({result.Reason})")}: {command}");
        };
        hud.CommandFeedbackObserved += feedback =>
        {
            if (feedback.Kind is not (FeedbackKind.InvalidOrder or FeedbackKind.Unsupported)) return;
            _invalidOrders++;
            Log($"feedback {feedback.Kind}{(feedback.Detail.Length > 0 ? $" ({feedback.Detail})" : string.Empty)}");
        };
        game.TickStepped += result =>
        {
            foreach (var simEvent in result.Events)
            {
                if (simEvent.Kind == SimEventKind.CommandRejected) Log($"simulation event CommandRejected: {simEvent}");
                if (simEvent.Kind == SimEventKind.ConstructionCompleted && _sites.TryGetValue(simEvent.Entity.Value, out var name) && _completedSites.Add(simEvent.Entity.Value))
                    Log($"construction completed: {name}#{simEvent.Entity.Value}");
            }
            foreach (var entity in game.World.Entities)
            {
                if (entity.Owner != game.Human || !entity.HasStatus(ObjectStatusBits.UnderConstruction) || !_sites.TryAdd(entity.Id.Value, entity.Template.Name)) continue;
                Log(string.Create(CultureInfo.InvariantCulture, $"construction site created: {entity.Template.Name}#{entity.Id.Value} at {(float)entity.X.ToDecimal():F0},{(float)entity.Y.ToDecimal():F0}"));
            }
        };
    }

    /// <summary>The input events of runtime frame <paramref name="frame"/>, shaped like the device translator's.</summary>
    public List<InputEvent> EventsFor(int frame, long timeMs)
    {
        _frame = frame;
        var events = new List<InputEvent>();
        if (_game is not { } game || _hud is not { } hud) return events;
        var buttons = new List<(MouseButton Button, bool Down)>();
        var keys = new List<InputEvent>();
        foreach (var step in _steps)
        {
            if (step.Frame != frame) continue;
            switch (step.Verb)
            {
                case "look":
                    if (TryWorld(step.Arguments, game, hud, out var lookAt, out var lookNote))
                    {
                        game.Camera.CenterOn(new Vector2(lookAt.X, lookAt.Y));
                        Log(string.Create(CultureInfo.InvariantCulture, $"look {step.Arguments} -> camera centred on {lookAt.X:F0},{lookAt.Y:F0}{lookNote}"));
                    }
                    else Unresolvable(step, lookNote);
                    break;
                case "move":
                    if (TryScreen(step.Arguments, game, hud, out var screen, out var moveNote))
                    {
                        _pointer = screen;
                        Log(string.Create(CultureInfo.InvariantCulture, $"move {step.Arguments} -> pixel {screen.X:F0},{screen.Y:F0}{moveNote}"));
                    }
                    else Unresolvable(step, moveNote);
                    break;
                case "down" or "up":
                {
                    var (button, target) = SplitButton(step.Arguments);
                    if (target.Length > 0)
                    {
                        if (TryScreen(target, game, hud, out var at, out var note)) _pointer = at;
                        else { Unresolvable(step, note); break; }
                    }
                    if (float.IsNaN(_pointer.X)) { Unresolvable(step, "no pointer position yet"); break; }
                    buttons.Add((Enum.Parse<MouseButton>(button, true), step.Verb == "down"));
                    break;
                }
                case "key":
                {
                    var key = step.Arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    keys.Add(new KeyEvent(timeMs, Enum.Parse<KeyCode>(key[1], true), key[0] == "down"));
                    Log($"key {key[0]} {key[1]}");
                    break;
                }
            }
        }

        if (!float.IsNaN(_pointer.X) && _pointer != _lastSent)
        {
            _lastSent = _pointer;
            events.Add(new MouseMoveEvent(timeMs, _pointer));
        }
        events.AddRange(keys);
        foreach (var (button, down) in buttons)
        {
            events.Add(new MouseButtonEvent(timeMs, button, down, _pointer));
            Log(string.Create(CultureInfo.InvariantCulture, $"{(down ? "down" : "up")} {button} at pixel {_pointer.X:F0},{_pointer.Y:F0}"));
        }
        _events += events.Count;
        return events;
    }

    /// <summary>After the frame's input and update: records what the input changed (command mode, placement ghost).</summary>
    public void Observe()
    {
        if (_hud is not { } hud) return;
        var commands = hud.Input.Commands;
        if (commands.Mode != _mode)
        {
            Log($"command mode {_mode} -> {commands.Mode}{(commands.PendingButton is { } button ? $" ({button.Kind} {button.Object})" : string.Empty)}");
            _mode = commands.Mode;
        }
        var ghost = commands.Ghost is { } value ? $"{value.Template} {(value.IsValid ? "valid" : $"invalid ({value.Reason})")}{(value.Anchored ? " anchored" : string.Empty)}" : "none";
        if (ghost != _ghost)
        {
            Log($"placement ghost {ghost}; overlay {(hud.Overlay.Placement.Active ? $"drawn with {hud.Overlay.Placement.FootprintScreen.Length} corners" : "not drawn")}");
            _ghost = ghost;
        }
    }

    /// <summary>The report for the console: every step and what followed, then the state at exit and one summary line a regression check can parse.</summary>
    public IReadOnlyList<string> Report()
    {
        var lines = new List<string>(_log);
        if (_game is { } game && _hud is { } hud)
        {
            foreach (var (id, name) in _sites.OrderBy(pair => pair.Key))
            {
                var entity = game.World.FindEntity(new CnCNew.Simulation.Ids.EntityId(id));
                var state = entity is null ? "gone" : entity.HasStatus(ObjectStatusBits.UnderConstruction) ? $"under construction (work {entity.ConstructionWork})" : "complete";
                lines.Add($"input-script: at exit site {name}#{id}: {state}");
            }
            var selected = string.Join(", ", hud.Input.Selection.Selected.Select(id => game.World.FindEntity(id) is { } entity ? $"{entity.Template.Name}#{id.Value}" : $"#{id.Value}"));
            lines.Add($"input-script: at exit tick {game.Tick}, selection [{selected}], command mode {hud.Input.Commands.Mode}, menu {(hud.IsMenuOpen ? "open" : "closed")}, paused {game.Paused}");
        }
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"input-script summary: steps={_steps.Count} events={_events} unresolved={_unresolved} selectionChanges={_selectionChanges} commandsAccepted={_accepted} commandsRejected={_rejected} invalidOrders={_invalidOrders} sitesCreated={_sites.Count} sitesCompleted={_completedSites.Count}"));
        return lines;
    }

    private void Log(string text) => _log.Add($"input-script: frame {_frame} tick {_game?.Tick ?? 0}: {text}");

    private void Unresolvable(Step step, string reason)
    {
        _unresolved++;
        Log($"UNRESOLVED line {step.Line} '{step.Verb} {step.Arguments}': {reason}");
    }

    // ------------------------------------------------------------------------------------------------ targets

    private static bool TryScreen(string target, LiveGame game, MatchController hud, out Vector2 screen, out string note)
    {
        screen = default;
        note = string.Empty;
        if (target.StartsWith("screen:", StringComparison.OrdinalIgnoreCase))
            return TryPair(target["screen:".Length..], out screen) || Fail("expected screen:X,Y", out note);
        if (target.StartsWith("button:", StringComparison.OrdinalIgnoreCase))
        {
            var name = target["button:".Length..];
            if (hud.WindowRect(name) is not { } rect) return Fail($"no HUD window '{name}'", out note);
            screen = new Vector2(rect.CenterX, rect.CenterY);
            note = $" (window {rect.X},{rect.Y} {rect.Width}x{rect.Height})";
            return true;
        }
        if (target.StartsWith("command:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = target["command:".Length..].Split(':', 2);
            if (!Enum.TryParse<GuiCommandKind>(parts[0], true, out var kind)) return Fail($"unknown command kind '{parts[0]}'", out note);
            for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
            {
                if (hud.CommandBar[slot] is not { IsEnabled: true } button || button.Button.Kind != kind) continue;
                if (parts.Length > 1 && !button.Button.Object.Equals(parts[1], StringComparison.OrdinalIgnoreCase)) continue;
                var window = $"ControlBar.wnd:ButtonCommand{slot:00}";
                if (hud.WindowRect(window) is not { } rect) return Fail($"no HUD window '{window}'", out note);
                screen = new Vector2(rect.CenterX, rect.CenterY);
                note = $" (slot {slot}: {button.Button.Kind} {button.Button.Object}, window {rect.X},{rect.Y} {rect.Width}x{rect.Height})";
                return true;
            }
            var shown = string.Join(", ", Enumerable.Range(1, CommandBarModel.GridSlots).Select(slot => hud.CommandBar[slot]).OfType<CommandBarButton>()
                .Select(button => $"{button.Slot}:{button.Button.Kind}:{button.Button.Object}:{button.Availability}"));
            return Fail($"no enabled command bar slot matches (context {hud.CommandBar.Context}; slots [{shown}])", out note);
        }
        if (!TryWorld(target, game, hud, out var world, out note)) return false;
        if (!game.Camera.TryWorldToScreen(world, out screen)) return Fail(string.Create(CultureInfo.InvariantCulture, $"world point {world.X:F0},{world.Y:F0} is not on screen"), out note);
        note = string.Create(CultureInfo.InvariantCulture, $" (world {world.X:F0},{world.Y:F0}{note})");
        return true;
    }

    private static bool TryWorld(string target, LiveGame game, MatchController hud, out Vector3 world, out string note)
    {
        world = default;
        note = string.Empty;
        var from = target.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
        var head = from >= 0 ? target[..from].Trim() : target;
        Entity? origin = null;
        if (from >= 0 && !TryEntity(target[(from + " from ".Length)..].Trim(), game, out origin, out note)) return false;

        if (head.Equals("placement", StringComparison.OrdinalIgnoreCase))
        {
            if (origin is null) return Fail("expected 'placement from ENTITY'", out note);
            var commands = hud.Input.Commands;
            if (commands.Mode != CommandModeKind.Placement || commands.PendingButton is not { } button) return Fail($"no placement is pending (command mode {commands.Mode})", out note);
            var barTop = hud.WindowRect("ControlBar.wnd:ControlBarParent")?.Y ?? int.MaxValue;
            var ground = SimulationQueries.GroundOf(origin);
            var reasons = new List<string>();
            // The building is placed with the angle the placement shows (the template's PlacementViewAngle until the player drags a direction), not with angle 0.
            var angle = commands.Ghost?.Angle ?? 0f;
            foreach (var offset in PlacementOffsets)
            {
                var candidate = ground + offset;
                var issue = SimulationQueries.CheckPlacement(game.World, game.Human, origin, button.Object, SimulationQueries.ToFixed(candidate.X), SimulationQueries.ToFixed(candidate.Y), SimulationQueries.ToFixed(angle));
                var point = new Vector3(candidate.X, candidate.Y, game.Heights.HeightAt(candidate.X, candidate.Y));
                if (issue == PlacementIssue.None && game.Camera.TryWorldToScreen(point, out var at) && at.X >= 0 && at.Y >= 0 && at.X < game.Camera.ViewportSize.X && at.Y < barTop)
                {
                    world = point;
                    note = $", {button.Object}";
                    return true;
                }
                reasons.Add(issue == PlacementIssue.None ? "off screen" : issue.ToString());
            }
            return Fail($"no spot for {button.Object} around {origin.Template.Name}#{origin.Id.Value} ({string.Join(", ", reasons)})", out note);
        }
        if (head.StartsWith("ground:", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryPair(head["ground:".Length..], out var pair)) return Fail("expected ground:X,Y", out note);
            if (origin is not null) pair += SimulationQueries.GroundOf(origin);
            world = new Vector3(pair.X, pair.Y, game.Heights.HeightAt(pair.X, pair.Y));
            return true;
        }
        if (from < 0 && TryEntity(head, game, out var entity, out note))
        {
            world = EntityPicker.CenterOf(entity!);
            note = $", {entity!.Template.Name}#{entity.Id.Value}";
            return true;
        }
        return note.Length > 0 ? false : Fail($"unknown target '{target}'", out note);
    }

    private static bool TryEntity(string target, LiveGame game, out Entity? entity, out string note)
    {
        entity = null;
        note = string.Empty;
        var byKind = target.StartsWith("own:", StringComparison.OrdinalIgnoreCase);
        if (!byKind && !target.StartsWith("template:", StringComparison.OrdinalIgnoreCase)) return Fail($"unknown target '{target}'", out note);
        var name = target[(target.IndexOf(':') + 1)..];
        var ordinal = 1;
        var hash = name.IndexOf('#');
        if (hash >= 0)
        {
            if (!int.TryParse(name[(hash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out ordinal) || ordinal < 1) return Fail($"bad object number in '{target}'", out note);
            name = name[..hash];
        }
        var matches = game.World.Entities
            .Where(candidate => candidate.Owner == game.Human && SimulationQueries.IsAlive(candidate) && (byKind ? candidate.Template.IsKindOf(name) : candidate.Template.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(candidate => candidate.Id.Value).ToList();
        if (matches.Count < ordinal) return Fail($"the human owns {matches.Count} living object(s) matching '{target}'", out note);
        entity = matches[ordinal - 1];
        return true;
    }

    private static bool TryPair(string text, out Vector2 pair)
    {
        pair = default;
        var parts = text.Split(',');
        if (parts.Length != 2 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
        pair = new Vector2(x, y);
        return true;
    }

    private static bool Fail(string reason, out string note)
    {
        note = reason;
        return false;
    }
}

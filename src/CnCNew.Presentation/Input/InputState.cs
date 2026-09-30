using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.Presentation.Input;

/// <summary>
/// Click, drag and double-click thresholds. Defaults: <see cref="DragTolerancePixels"/> and <see cref="ClickMaxMilliseconds"/> are the retail
/// <c>Mouse.ini</c> values (<c>DragTolerance = 25</c>, <c>DragToleranceMS = 250</c>); the double-click window and distance are the Windows
/// defaults the original inherits from <c>WM_LBUTTONDBLCLK</c> (500 ms, 4 px); <see cref="MiddleClickMaxMilliseconds"/> is <c>LookAtXlat.cpp</c>'s
/// camera-reset click limit.
/// </summary>
public sealed record InputSettings(
    float DragTolerancePixels = 25f,
    long ClickMaxMilliseconds = 250,
    long DoubleClickMilliseconds = 500,
    float DoubleClickDistancePixels = 4f,
    long MiddleClickMaxMilliseconds = 167);

public enum GestureKind
{
    Click,
    DoubleClick,
    DragStarted,
    DragMoved,
    DragEnded,
    DragCancelled,
}

/// <summary>An axis-aligned screen rectangle in pixels.</summary>
public readonly record struct ScreenRect(Vector2 Min, Vector2 Max)
{
    public static ScreenRect FromCorners(Vector2 a, Vector2 b) => new(Vector2.Min(a, b), Vector2.Max(a, b));

    public bool Contains(Vector2 point) => point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
}

/// <summary>A button gesture derived from raw events: <see cref="Start"/> is where the button went down, <see cref="Position"/> the current or release point.</summary>
public sealed record PointerGesture(GestureKind Kind, MouseButton Button, Vector2 Start, Vector2 Position, KeyModifiers Modifiers, long TimeMs)
{
    public ScreenRect Rect => ScreenRect.FromCorners(Start, Position);
}

/// <summary>
/// The held keys and buttons, the mouse position, and the click/drag/double-click state machine of <c>MetaEventTranslator::onMouseEvent</c> and
/// <c>SelectionTranslator</c> (a press becomes a drag once it moves more than the tolerance on either axis, <c>SelectionXlat.cpp</c>). Pure: every
/// decision depends only on the events and their caller-supplied times.
/// </summary>
public sealed class InputState(InputSettings? settings = null)
{
    private const int ButtonCount = 3;

    private readonly HashSet<KeyCode> _keys = [];
    private readonly ButtonTrack[] _buttons = [new(), new(), new()];

    private sealed class ButtonTrack
    {
        public bool Down;
        public bool Dragging;
        public Vector2 Start;
        public long DownTime;
        public bool HasLastClick;
        public long LastClickTime;
        public Vector2 LastClickPosition;
    }

    public InputSettings Settings { get; } = settings ?? new InputSettings();
    public Vector2 MousePosition { get; private set; }
    public bool HasMouse { get; private set; }
    public long LastTime { get; private set; }

    public KeyModifiers Modifiers
    {
        get
        {
            var modifiers = KeyModifiers.None;
            foreach (var key in _keys) modifiers |= KeyNames.ModifierOf(key);
            return modifiers;
        }
    }

    public bool IsKeyDown(KeyCode key) => _keys.Contains(key);
    public bool IsButtonDown(MouseButton button) => _buttons[(int)button].Down;
    public bool IsDragging(MouseButton button) => _buttons[(int)button].Dragging;

    /// <summary>Where the button went down, while it is held.</summary>
    public Vector2? PressPosition(MouseButton button) => _buttons[(int)button].Down ? _buttons[(int)button].Start : null;

    /// <summary>The keys currently held, in enum order (deterministic).</summary>
    public ImmutableArray<KeyCode> HeldKeys => [.._keys.Order()];

    public ImmutableArray<PointerGesture> Apply(InputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        LastTime = inputEvent.TimeMs;
        var gestures = ImmutableArray.CreateBuilder<PointerGesture>();
        switch (inputEvent)
        {
            case MouseMoveEvent move:
                Move(move.Position, move.TimeMs, gestures);
                break;
            case MouseButtonEvent button:
                Move(button.Position, button.TimeMs, gestures);
                if (button.IsDown) Press(button.Button, button.Position, button.TimeMs);
                else Release(button.Button, button.Position, button.TimeMs, gestures);
                break;
            case MouseWheelEvent wheel:
                Move(wheel.Position, wheel.TimeMs, gestures);
                break;
            case KeyEvent key:
                if (key.IsDown) _keys.Add(key.Key);
                else _keys.Remove(key.Key);
                break;
            case FocusLostEvent:
                for (var index = 0; index < ButtonCount; index++)
                {
                    var track = _buttons[index];
                    if (track.Dragging) gestures.Add(new PointerGesture(GestureKind.DragCancelled, (MouseButton)index, track.Start, MousePosition, Modifiers, inputEvent.TimeMs));
                    track.Down = false;
                    track.Dragging = false;
                    track.HasLastClick = false;
                }
                _keys.Clear();
                break;
        }
        return gestures.ToImmutable();
    }

    private void Move(Vector2 position, long time, ImmutableArray<PointerGesture>.Builder gestures)
    {
        MousePosition = position;
        HasMouse = true;
        for (var index = 0; index < ButtonCount; index++)
        {
            var track = _buttons[index];
            if (!track.Down) continue;
            if (track.Dragging)
            {
                gestures.Add(new PointerGesture(GestureKind.DragMoved, (MouseButton)index, track.Start, position, Modifiers, time));
                continue;
            }
            var delta = Vector2.Abs(position - track.Start);
            if (delta.X > Settings.DragTolerancePixels || delta.Y > Settings.DragTolerancePixels)
            {
                track.Dragging = true;
                gestures.Add(new PointerGesture(GestureKind.DragStarted, (MouseButton)index, track.Start, position, Modifiers, time));
            }
        }
    }

    private void Press(MouseButton button, Vector2 position, long time)
    {
        var track = _buttons[(int)button];
        track.Down = true;
        track.Dragging = false;
        track.Start = position;
        track.DownTime = time;
    }

    private void Release(MouseButton button, Vector2 position, long time, ImmutableArray<PointerGesture>.Builder gestures)
    {
        var track = _buttons[(int)button];
        if (!track.Down) return;
        track.Down = false;
        if (track.Dragging)
        {
            track.Dragging = false;
            track.HasLastClick = false;
            gestures.Add(new PointerGesture(GestureKind.DragEnded, button, track.Start, position, Modifiers, time));
            return;
        }
        var held = time - track.DownTime;
        if (button == MouseButton.Right && held > Settings.ClickMaxMilliseconds) return;
        if (button == MouseButton.Middle && held > Settings.MiddleClickMaxMilliseconds) return;
        var isDouble = track.HasLastClick
                       && track.DownTime - track.LastClickTime <= Settings.DoubleClickMilliseconds
                       && Vector2.Distance(track.LastClickPosition, track.Start) <= Settings.DoubleClickDistancePixels;
        // A double click consumes the pair, so a third click starts a new pair (as the OS double-click message does).
        track.HasLastClick = !isDouble;
        track.LastClickTime = time;
        track.LastClickPosition = position;
        gestures.Add(new PointerGesture(isDouble ? GestureKind.DoubleClick : GestureKind.Click, button, track.Start, position, Modifiers, time));
    }
}

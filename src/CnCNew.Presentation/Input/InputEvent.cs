using System.Numerics;

namespace CnCNew.Presentation.Input;

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>
/// One raw device event, already translated from the client's device API. <see cref="TimeMs"/> is a monotonic millisecond clock supplied by
/// the caller; the input layer never reads wall time itself. Positions are viewport pixels (origin top-left).
/// </summary>
public abstract record InputEvent(long TimeMs);

public sealed record MouseMoveEvent(long TimeMs, Vector2 Position) : InputEvent(TimeMs);

public sealed record MouseButtonEvent(long TimeMs, MouseButton Button, bool IsDown, Vector2 Position) : InputEvent(TimeMs);

/// <summary><paramref name="Notches"/>: wheel detents, positive away from the user (<c>wheelDelta / MOUSE_WHEEL_DELTA</c>).</summary>
public sealed record MouseWheelEvent(long TimeMs, float Notches, Vector2 Position) : InputEvent(TimeMs);

/// <summary><paramref name="IsRepeat"/>: an auto-repeated down event of a key that is already held.</summary>
public sealed record KeyEvent(long TimeMs, KeyCode Key, bool IsDown, bool IsRepeat = false) : InputEvent(TimeMs);

/// <summary>Typed text (chat, name entry); the game input layer ignores it, the UI layer consumes it.</summary>
public sealed record TextInputEvent(long TimeMs, char Character) : InputEvent(TimeMs);

/// <summary>The window lost focus: every held key and button counts as released, a drag in progress is cancelled.</summary>
public sealed record FocusLostEvent(long TimeMs) : InputEvent(TimeMs);

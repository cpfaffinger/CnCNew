using System.Numerics;
using Stride.Input;
using PresentationInput = CnCNew.Presentation.Input;

namespace CnCNew.Client.Stride.Ui;

/// <summary>
/// Turns one frame of Stride input (<see cref="InputManager.Events"/> and the absolute mouse position) into the device-neutral
/// <see cref="PresentationInput.InputEvent"/>s the presentation layer and the shell consume. Positions are window pixels with the origin top-left, which is the
/// back-buffer space the UI is laid out in. Keys the input layer has no name for are dropped (they cannot be bound), text goes through as characters.
/// </summary>
internal sealed class StrideInputTranslator
{
    private Vector2 _lastPosition = new(float.NaN, float.NaN);

    /// <summary>The events of this frame in the order Stride delivered them; a move event first when the pointer moved.</summary>
    public List<PresentationInput.InputEvent> Poll(InputManager input, long timeMs)
    {
        var events = new List<PresentationInput.InputEvent>();
        var position = new Vector2(input.AbsoluteMousePosition.X, input.AbsoluteMousePosition.Y);
        if (input.HasMouse && position != _lastPosition)
        {
            _lastPosition = position;
            events.Add(new PresentationInput.MouseMoveEvent(timeMs, position));
        }
        foreach (var inputEvent in input.Events)
        {
            switch (inputEvent)
            {
                case KeyEvent key when Map(key.Key) is var code && code != PresentationInput.KeyCode.None:
                    events.Add(new PresentationInput.KeyEvent(timeMs, code, key.IsDown, key.RepeatCount > 0));
                    break;
                case TextInputEvent text when text.Type == TextInputEventType.Input:
                    foreach (var character in text.Text) events.Add(new PresentationInput.TextInputEvent(timeMs, character));
                    break;
                case MouseButtonEvent button when MapButton(button.Button) is { } mapped:
                    events.Add(new PresentationInput.MouseButtonEvent(timeMs, mapped, button.IsDown, position));
                    break;
                case MouseWheelEvent wheel:
                    events.Add(new PresentationInput.MouseWheelEvent(timeMs, wheel.WheelDelta, position));
                    break;
            }
        }
        return events;
    }

    private static PresentationInput.MouseButton? MapButton(global::Stride.Input.MouseButton button) => button switch
    {
        global::Stride.Input.MouseButton.Left => PresentationInput.MouseButton.Left,
        global::Stride.Input.MouseButton.Right => PresentationInput.MouseButton.Right,
        global::Stride.Input.MouseButton.Middle => PresentationInput.MouseButton.Middle,
        _ => null,
    };

    /// <summary>The key table: letters, digits, keypad, function keys, navigation, the punctuation of <c>KeyNames</c> and the modifier keys.</summary>
    internal static PresentationInput.KeyCode Map(Keys key)
    {
        if (key is >= Keys.A and <= Keys.Z) return PresentationInput.KeyCode.A + (key - Keys.A);
        if (key is >= Keys.D1 and <= Keys.D9) return PresentationInput.KeyCode.D1 + (key - Keys.D1);
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9) return PresentationInput.KeyCode.Keypad0 + (key - Keys.NumPad0);
        if (key is >= Keys.F1 and <= Keys.F12) return PresentationInput.KeyCode.F1 + (key - Keys.F1);
        return key switch
        {
            Keys.D0 => PresentationInput.KeyCode.D0,
            Keys.Escape => PresentationInput.KeyCode.Escape,
            Keys.Back => PresentationInput.KeyCode.Backspace,
            Keys.Enter => PresentationInput.KeyCode.Enter,
            Keys.Space => PresentationInput.KeyCode.Space,
            Keys.Tab => PresentationInput.KeyCode.Tab,
            Keys.Up => PresentationInput.KeyCode.Up,
            Keys.Down => PresentationInput.KeyCode.Down,
            Keys.Left => PresentationInput.KeyCode.Left,
            Keys.Right => PresentationInput.KeyCode.Right,
            Keys.Home => PresentationInput.KeyCode.Home,
            Keys.End => PresentationInput.KeyCode.End,
            Keys.PageUp => PresentationInput.KeyCode.PageUp,
            Keys.PageDown => PresentationInput.KeyCode.PageDown,
            Keys.Insert => PresentationInput.KeyCode.Insert,
            Keys.Delete => PresentationInput.KeyCode.Delete,
            Keys.OemMinus => PresentationInput.KeyCode.Minus,
            Keys.OemPlus => PresentationInput.KeyCode.Equal,
            Keys.OemOpenBrackets => PresentationInput.KeyCode.LeftBracket,
            Keys.OemCloseBrackets => PresentationInput.KeyCode.RightBracket,
            Keys.OemSemicolon => PresentationInput.KeyCode.Semicolon,
            Keys.OemQuotes => PresentationInput.KeyCode.Apostrophe,
            Keys.OemTilde => PresentationInput.KeyCode.Tick,
            Keys.OemPipe or Keys.OemBackslash => PresentationInput.KeyCode.Backslash,
            Keys.OemComma => PresentationInput.KeyCode.Comma,
            Keys.OemPeriod => PresentationInput.KeyCode.Period,
            Keys.OemQuestion => PresentationInput.KeyCode.Slash,
            Keys.Multiply => PresentationInput.KeyCode.KeypadStar,
            Keys.Subtract => PresentationInput.KeyCode.KeypadMinus,
            Keys.Add => PresentationInput.KeyCode.KeypadPlus,
            Keys.Divide => PresentationInput.KeyCode.KeypadSlash,
            Keys.Decimal => PresentationInput.KeyCode.KeypadDelete,
            Keys.LeftShift => PresentationInput.KeyCode.LeftShift,
            Keys.RightShift => PresentationInput.KeyCode.RightShift,
            Keys.LeftCtrl => PresentationInput.KeyCode.LeftControl,
            Keys.RightCtrl => PresentationInput.KeyCode.RightControl,
            Keys.LeftAlt => PresentationInput.KeyCode.LeftAlt,
            Keys.RightAlt => PresentationInput.KeyCode.RightAlt,
            _ => PresentationInput.KeyCode.None,
        };
    }
}

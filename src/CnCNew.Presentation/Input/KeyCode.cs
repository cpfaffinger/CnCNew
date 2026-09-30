using System.Collections.Frozen;

namespace CnCNew.Presentation.Input;

/// <summary>
/// The keys the input layer knows: every mappable key of <c>MetaEvent.h</c> (<c>MappableKeyType</c>/<c>KeyNames</c>) plus the six modifier
/// keys, which are never mapped themselves but drive <see cref="KeyModifiers"/>. The client translates its device keys into these.
/// </summary>
public enum KeyCode
{
    None = 0,
    Keypad0, Keypad1, Keypad2, Keypad3, Keypad4, Keypad5, Keypad6, Keypad7, Keypad8, Keypad9,
    KeypadDelete, KeypadStar, KeypadMinus, KeypadPlus, KeypadEnter, KeypadSlash,
    Escape, Backspace, Enter, Space, Tab,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D1, D2, D3, D4, D5, D6, D7, D8, D9, D0,
    Minus, Equal, LeftBracket, RightBracket, Semicolon, Apostrophe, Tick, Backslash, Comma, Period, Slash,
    Up, Down, Left, Right, Home, End, PageUp, PageDown, Insert, Delete,
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
}

/// <summary><c>MappableKeyModState</c>: which of CTRL, ALT, SHIFT are held (left and right keys are equivalent).</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

/// <summary>The INI spelling of keys and modifier states (<c>KeyNames</c>, <c>ModifierNames</c> in <c>MetaEvent.h</c>); lookups ignore case like <c>INI::scanLookupList</c>.</summary>
public static class KeyNames
{
    private static readonly FrozenDictionary<string, KeyCode> ByName = BuildNames().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    private static readonly FrozenDictionary<KeyCode, string> ByKey = BuildNames().ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    private static readonly (string Name, KeyModifiers Modifiers)[] ModifierTable =
    [
        ("NONE", KeyModifiers.None), ("CTRL", KeyModifiers.Control), ("ALT", KeyModifiers.Alt), ("SHIFT", KeyModifiers.Shift),
        ("CTRL_ALT", KeyModifiers.Control | KeyModifiers.Alt), ("SHIFT_CTRL", KeyModifiers.Shift | KeyModifiers.Control),
        ("SHIFT_ALT", KeyModifiers.Shift | KeyModifiers.Alt), ("SHIFT_ALT_CTRL", KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control),
    ];

    private static IEnumerable<KeyValuePair<string, KeyCode>> BuildNames()
    {
        for (var index = 0; index <= 9; index++) yield return new($"KEY_KP{index}", KeyCode.Keypad0 + index);
        yield return new("KEY_KPDEL", KeyCode.KeypadDelete);
        yield return new("KEY_KPSTAR", KeyCode.KeypadStar);
        yield return new("KEY_KPMINUS", KeyCode.KeypadMinus);
        yield return new("KEY_KPPLUS", KeyCode.KeypadPlus);
        yield return new("KEY_KPENTER", KeyCode.KeypadEnter);
        yield return new("KEY_KPSLASH", KeyCode.KeypadSlash);
        yield return new("KEY_ESC", KeyCode.Escape);
        yield return new("KEY_BACKSPACE", KeyCode.Backspace);
        yield return new("KEY_ENTER", KeyCode.Enter);
        yield return new("KEY_SPACE", KeyCode.Space);
        yield return new("KEY_TAB", KeyCode.Tab);
        for (var index = 1; index <= 12; index++) yield return new($"KEY_F{index}", KeyCode.F1 + index - 1);
        for (var letter = 'A'; letter <= 'Z'; letter++) yield return new($"KEY_{letter}", KeyCode.A + (letter - 'A'));
        for (var digit = 1; digit <= 9; digit++) yield return new($"KEY_{digit}", KeyCode.D1 + digit - 1);
        yield return new("KEY_0", KeyCode.D0);
        yield return new("KEY_MINUS", KeyCode.Minus);
        yield return new("KEY_EQUAL", KeyCode.Equal);
        yield return new("KEY_LBRACKET", KeyCode.LeftBracket);
        yield return new("KEY_RBRACKET", KeyCode.RightBracket);
        yield return new("KEY_SEMICOLON", KeyCode.Semicolon);
        yield return new("KEY_APOSTROPHE", KeyCode.Apostrophe);
        yield return new("KEY_TICK", KeyCode.Tick);
        yield return new("KEY_BACKSLASH", KeyCode.Backslash);
        yield return new("KEY_COMMA", KeyCode.Comma);
        yield return new("KEY_PERIOD", KeyCode.Period);
        yield return new("KEY_SLASH", KeyCode.Slash);
        yield return new("KEY_UP", KeyCode.Up);
        yield return new("KEY_DOWN", KeyCode.Down);
        yield return new("KEY_LEFT", KeyCode.Left);
        yield return new("KEY_RIGHT", KeyCode.Right);
        yield return new("KEY_HOME", KeyCode.Home);
        yield return new("KEY_END", KeyCode.End);
        yield return new("KEY_PGUP", KeyCode.PageUp);
        yield return new("KEY_PGDN", KeyCode.PageDown);
        yield return new("KEY_INS", KeyCode.Insert);
        yield return new("KEY_DEL", KeyCode.Delete);
        yield return new("KEY_NONE", KeyCode.None);
    }

    public static bool TryParseKey(string name, out KeyCode key) => ByName.TryGetValue(name.Trim(), out key);

    /// <summary>The INI name of a mappable key; modifier keys have none.</summary>
    public static bool TryGetName(KeyCode key, out string name) => ByKey.TryGetValue(key, out name!);

    public static bool TryParseModifiers(string name, out KeyModifiers modifiers)
    {
        foreach (var (candidate, value) in ModifierTable)
            if (candidate.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) { modifiers = value; return true; }
        modifiers = KeyModifiers.None;
        return false;
    }

    public static string ModifierName(KeyModifiers modifiers)
    {
        foreach (var (candidate, value) in ModifierTable)
            if (value == modifiers) return candidate;
        throw new ArgumentOutOfRangeException(nameof(modifiers));
    }

    public static bool IsModifier(KeyCode key) => key is >= KeyCode.LeftShift and <= KeyCode.RightAlt;

    public static KeyModifiers ModifierOf(KeyCode key) => key switch
    {
        KeyCode.LeftShift or KeyCode.RightShift => KeyModifiers.Shift,
        KeyCode.LeftControl or KeyCode.RightControl => KeyModifiers.Control,
        KeyCode.LeftAlt or KeyCode.RightAlt => KeyModifiers.Alt,
        _ => KeyModifiers.None,
    };

    /// <summary>The letter or digit a key types, for control-bar hotkeys (<c>HotKey.cpp</c> compares lowercase characters).</summary>
    public static char? CharacterOf(KeyCode key) => key switch
    {
        >= KeyCode.A and <= KeyCode.Z => (char)('a' + (key - KeyCode.A)),
        >= KeyCode.D1 and <= KeyCode.D9 => (char)('1' + (key - KeyCode.D1)),
        KeyCode.D0 => '0',
        _ => null,
    };
}

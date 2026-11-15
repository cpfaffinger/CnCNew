using CnCNew.Presentation.Input;

namespace CnCNew.Game.Hud;

/// <summary>
/// A key as a person reads it, and the line a tooltip of the HUD names it with (#388; a feature of this client, English only: the original's popup underlines the letter
/// of a command button's label instead and names no key of the bar's other controls).
/// </summary>
public static class HotkeyText
{
    /// <summary>The line under a control's description.</summary>
    public const string Prefix = "Hotkey: ";

    /// <summary>The tooltip <paramref name="text"/> with the line that names <paramref name="key"/> below it; the text alone when there is no key.</summary>
    public static string Append(string text, string? key) => key is { Length: > 0 } ? text + "\n" + Prefix + key : text;

    /// <summary>A command button's letter (<c>HotKeyManager::searchHotKey</c>: the character after the label's <c>&amp;</c>) as it is printed on the key.</summary>
    public static string Letter(char letter) => char.ToUpperInvariant(letter).ToString();

    /// <summary>A key as a person reads it: <c>Ctrl+Alt+F1</c>; a binding of the modifiers alone says so; a binding on the release of a key says that.</summary>
    public static string Key(KeyBinding binding)
    {
        var parts = new List<string>();
        if ((binding.Modifiers & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((binding.Modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((binding.Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if (binding.Key != KeyCode.None)
        {
            var name = KeyNames.TryGetName(binding.Key, out var ini) ? ini : binding.Key.ToString();
            if (name.StartsWith("KEY_", StringComparison.Ordinal)) name = name[4..];
            name = name.StartsWith("KP", StringComparison.Ordinal) && name.Length > 2 ? "Keypad " + Words(name[2..]) : Words(name.Replace('_', ' '));
            parts.Add(name);
        }
        var text = parts.Count == 0 ? "-" : string.Join("+", parts) + (binding.Key == KeyCode.None ? " (held)" : string.Empty);
        return binding.Transition == KeyTransition.Up ? text + " (on release)" : text;
    }

    /// <summary><c>BeginCameraRotateLeft</c> as "Begin camera rotate left"; <c>LEFT</c> as "Left"; one letter or digit stays as it is.</summary>
    public static string Words(string name)
    {
        if (name.Length <= 1) return name;
        if (name.All(ch => !char.IsLower(ch))) return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => word.Length <= 1 ? word : word[0] + word[1..].ToLowerInvariant()));
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var ch = name[index];
            if (index > 0 && char.IsUpper(ch) && char.IsLower(name[index - 1])) builder.Append(' ').Append(char.ToLowerInvariant(ch));
            else if (index > 0 && char.IsDigit(ch) && !char.IsDigit(name[index - 1])) builder.Append(' ').Append(ch);
            else builder.Append(ch);
        }
        return builder.ToString();
    }
}

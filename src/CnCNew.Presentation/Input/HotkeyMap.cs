using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Input;

/// <summary><c>MappableKeyTransition</c>. <see cref="DoubleDown"/> parses but never fires: the reference comments its detection out (<c>MetaEvent.cpp</c>).</summary>
public enum KeyTransition
{
    Down,
    Up,
    DoubleDown,
}

/// <summary><c>CommandUsableInType</c> (<c>UseableIn</c> bit string).</summary>
[Flags]
public enum HotkeyContexts
{
    None = 0,
    Shell = 1,
    Game = 2,
    Observer = 4,
}

/// <summary>A key with its modifier state and transition. <see cref="KeyCode.None"/> binds the modifier state alone (<c>KEY_NONE</c>, used by <c>BEGIN_FORCEATTACK</c>).</summary>
public readonly record struct KeyBinding(KeyCode Key, KeyModifiers Modifiers, KeyTransition Transition = KeyTransition.Down)
{
    /// <summary>The settings spelling: <c>MODIFIERS KEY TRANSITION</c>, e.g. <c>CTRL KEY_1 DOWN</c>.</summary>
    public override string ToString()
        => $"{KeyNames.ModifierName(Modifiers)} {(KeyNames.TryGetName(Key, out var name) ? name : Key.ToString())} {Transition.ToString().ToUpperInvariant()}";

    public static bool TryParse(string text, out KeyBinding binding)
    {
        binding = default;
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !KeyNames.TryParseModifiers(parts[0], out var modifiers) || !KeyNames.TryParseKey(parts[1], out var key)
            || !Enum.TryParse<KeyTransition>(parts[2], ignoreCase: true, out var transition) || !Enum.IsDefined(transition))
            return false;
        binding = new KeyBinding(key, modifiers, transition);
        return true;
    }
}

public enum HotkeySource
{
    /// <summary>From <c>CommandMap.ini</c>.</summary>
    Ini,
    /// <summary>An engine default for an action the retail data does not bind (see <see cref="MetaAction"/>).</summary>
    Engine,
    /// <summary>Changed by the player (<see cref="HotkeyMap.Rebind"/>, <see cref="HotkeyMap.ApplySettings"/>).</summary>
    User,
}

/// <param name="Category">The <c>Category</c> name (<c>CategoryListName</c>), empty when absent.</param>
/// <param name="DisplayNameLabel">String-table label of <c>DisplayName</c>; the UI translates it.</param>
public sealed record HotkeyEntry(MetaAction Action, KeyBinding Binding, HotkeyContexts UsableIn, string Category, string DisplayNameLabel, string DescriptionLabel, HotkeySource Source);

public enum HotkeyIssueKind
{
    Syntax,
    UnknownAction,
    UnknownField,
    UnknownKey,
    UnknownTransition,
    UnknownModifiers,
    UnknownContext,
    UnknownCategory,
    MissingKey,
    BadSettingValue,
    /// <summary>
    /// Accepted, not an error: a field written <c>Name Value</c> without <c>=</c>. The original tokenizer treats <c>=</c> as one more separator
    /// (<c>INI::initFromINIMulti</c>), so the retail engine reads it; the retail <c>CommandMap.ini</c> has ten such lines.
    /// </summary>
    FieldWithoutEquals,
}

/// <summary>A <c>CommandMap.ini</c> or settings problem; the offending entry is skipped, never replaced by a guess.</summary>
public sealed record HotkeyIssue(HotkeyIssueKind Kind, string Subject, string Detail);

/// <summary>Two or more actions fire on the same key, modifiers and transition in overlapping contexts.</summary>
public sealed record HotkeyConflict(KeyBinding Binding, ImmutableArray<MetaAction> Actions);

/// <summary>
/// The keyboard map of <c>MetaMap</c> (<c>MetaEvent.cpp</c>, <c>TheMetaMapFieldParseTable</c>): one binding per action, the last definition of an
/// action wins (<c>MetaMap::parseMetaMap</c> reuses the record). Immutable; <see cref="Rebind"/> and <see cref="ApplySettings"/> return a new map.
/// </summary>
public sealed class HotkeyMap
{
    private static readonly string[] Categories = ["CONTROL", "INFORMATION", "INTERFACE", "SELECTION", "TAUNT", "TEAM", "MISC", "DEBUG"];
    public const string Unbound = "UNBOUND";

    private HotkeyMap(ImmutableArray<HotkeyEntry> entries, ImmutableArray<HotkeyIssue> issues, ImmutableArray<HotkeyIssue> compatibility)
    {
        Entries = [..entries.OrderBy(entry => entry.Action)];
        Issues = issues;
        Compatibility = compatibility;
        Conflicts = FindConflicts(Entries);
    }

    /// <summary>One entry per bound action, in <see cref="MetaAction"/> order.</summary>
    public ImmutableArray<HotkeyEntry> Entries { get; }
    /// <summary>Problems: the affected entry was skipped.</summary>
    public ImmutableArray<HotkeyIssue> Issues { get; }

    /// <summary>Accepted compatibility cases (<see cref="HotkeyIssueKind.FieldWithoutEquals"/>), counted so that they never pass unnoticed.</summary>
    public ImmutableArray<HotkeyIssue> Compatibility { get; }

    public ImmutableArray<HotkeyConflict> Conflicts { get; }

    public static HotkeyMap Empty { get; } = new([], [], []);

    public bool TryGetEntry(MetaAction action, out HotkeyEntry entry)
    {
        foreach (var candidate in Entries)
            if (candidate.Action == action) { entry = candidate; return true; }
        entry = null!;
        return false;
    }

    /// <summary>Every action bound to exactly this key, modifier state and transition that is usable in <paramref name="context"/>, in action order.</summary>
    public ImmutableArray<MetaAction> Resolve(KeyBinding pressed, HotkeyContexts context)
        => [..Entries.Where(entry => entry.Binding == pressed && (entry.UsableIn & context) != 0).Select(entry => entry.Action)];

    /// <summary>Parses <c>CommandMap.ini</c> text (<c>CommandMap NAME</c> ... <c>End</c> blocks). Later text wins for a repeated action.</summary>
    public static HotkeyMap Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var blocks = new List<(string Name, List<(string Key, string Value)> Fields)>();
        var issues = ImmutableArray.CreateBuilder<HotkeyIssue>();
        var compatibility = ImmutableArray.CreateBuilder<HotkeyIssue>();
        (string Name, List<(string Key, string Value)> Fields)? current = null;
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var comment = rawLine.IndexOf(';');
            var line = (comment >= 0 ? rawLine[..comment] : rawLine).Trim();
            if (line.Length == 0) continue;
            if (current is null)
            {
                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0].Equals("CommandMap", StringComparison.OrdinalIgnoreCase)) current = (parts[1], []);
                else issues.Add(new HotkeyIssue(HotkeyIssueKind.Syntax, $"line {lineNumber}", $"expected 'CommandMap <name>', found '{line}'"));
                continue;
            }
            if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(current.Value);
                current = null;
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals > 0)
            {
                current.Value.Fields.Add((line[..equals].Trim(), line[(equals + 1)..].Trim()));
                continue;
            }
            var space = line.IndexOfAny([' ', '\t']);
            if (equals < 0 && space > 0)
            {
                compatibility.Add(new HotkeyIssue(HotkeyIssueKind.FieldWithoutEquals, current.Value.Name, $"line {lineNumber}: '{line}'"));
                current.Value.Fields.Add((line[..space], line[space..].Trim()));
            }
            else issues.Add(new HotkeyIssue(HotkeyIssueKind.Syntax, $"line {lineNumber}", $"expected 'Field = Value', found '{line}'"));
        }
        if (current is not null) issues.Add(new HotkeyIssue(HotkeyIssueKind.Syntax, current.Value.Name, "block without End"));
        return Build(blocks, issues, compatibility.ToImmutable());
    }

    /// <summary>
    /// Reads the <c>CommandMap</c> blocks of a compiled definition graph: the compiler keeps them as compatibility definitions (id
    /// <c>commandmap/NAME</c>) with their original field text, so the result equals <see cref="Parse"/> of the loaded files.
    /// </summary>
    public static HotkeyMap FromGraph(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var blocks = graph.Definitions.OfType<CompatibilityDefinition>()
            .Where(definition => definition.Id.Name.StartsWith("commandmap/", StringComparison.Ordinal))
            .OrderBy(definition => definition.Id.Name, StringComparer.Ordinal)
            .Select(definition => (definition.OriginalName, definition.Fields.Select(field => (field.Name, field.Value.OriginalText.Trim())).ToList()))
            .ToList();
        return Build(blocks, ImmutableArray.CreateBuilder<HotkeyIssue>(), []);
    }

    private static HotkeyMap Build(List<(string Name, List<(string Key, string Value)> Fields)> blocks, ImmutableArray<HotkeyIssue>.Builder issues, ImmutableArray<HotkeyIssue> compatibility)
    {
        var entries = new Dictionary<MetaAction, HotkeyEntry>();
        foreach (var (name, fields) in blocks)
        {
            if (!MetaActionNames.TryParse(name, out var action) || MetaActionNames.IsEngineAction(action))
            {
                issues.Add(new HotkeyIssue(HotkeyIssueKind.UnknownAction, name, "not a retail meta event name"));
                continue;
            }
            KeyCode? key = null;
            var keyField = false;
            var modifiers = KeyModifiers.None;
            var transition = KeyTransition.Down;
            var usableIn = HotkeyContexts.None;
            string category = string.Empty, display = string.Empty, description = string.Empty;
            var valid = true;
            foreach (var (field, value) in fields)
            {
                switch (field.ToUpperInvariant())
                {
                    case "KEY":
                        keyField = true;
                        if (KeyNames.TryParseKey(value, out var parsedKey)) key = parsedKey;
                        else { issues.Add(new HotkeyIssue(HotkeyIssueKind.UnknownKey, name, value)); valid = false; }
                        break;
                    case "TRANSITION":
                        if (Enum.TryParse<KeyTransition>(value, ignoreCase: true, out var parsedTransition) && Enum.IsDefined(parsedTransition)) transition = parsedTransition;
                        else { issues.Add(new HotkeyIssue(HotkeyIssueKind.UnknownTransition, name, value)); valid = false; }
                        break;
                    case "MODIFIERS":
                        if (KeyNames.TryParseModifiers(value, out var parsedModifiers)) modifiers = parsedModifiers;
                        else { issues.Add(new HotkeyIssue(HotkeyIssueKind.UnknownModifiers, name, value)); valid = false; }
                        break;
                    case "USEABLEIN":
                        if (TryParseContexts(value, out var contexts)) usableIn = contexts;
                        else { issues.Add(new HotkeyIssue(HotkeyIssueKind.UnknownContext, name, value)); valid = false; }
                        break;
                    case "CATEGORY":
                        if (Categories.Contains(value, StringComparer.OrdinalIgnoreCase)) category = value.ToUpperInvariant();
                        else { issues.Add(new HotkeyIssue(HotkeyIssueKind.UnknownCategory, name, value)); valid = false; }
                        break;
                    case "DISPLAYNAME":
                        display = value;
                        break;
                    case "DESCRIPTION":
                        description = value;
                        break;
                    default:
                        issues.Add(new HotkeyIssue(HotkeyIssueKind.UnknownField, name, field));
                        valid = false;
                        break;
                }
            }
            if (!keyField)
            {
                issues.Add(new HotkeyIssue(HotkeyIssueKind.MissingKey, name, "no Key field"));
                valid = false;
            }
            if (valid) entries[action] = new HotkeyEntry(action, new KeyBinding(key!.Value, modifiers, transition), usableIn, category, display, description, HotkeySource.Ini);
        }
        return new HotkeyMap([..entries.Values], issues.ToImmutable(), compatibility);
    }

    private static bool TryParseContexts(string text, out HotkeyContexts contexts)
    {
        contexts = HotkeyContexts.None;
        foreach (var token in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase)) { contexts = HotkeyContexts.None; return true; }
            if (!Enum.TryParse<HotkeyContexts>(token, ignoreCase: true, out var flag) || flag is HotkeyContexts.None || !Enum.IsDefined(flag)) return false;
            contexts |= flag;
        }
        return true;
    }

    /// <summary>
    /// Adds the engine bindings for actions the retail map leaves unbound: the four arrow keys scroll (<c>LookAtXlat.cpp</c> reads them raw).
    /// <see cref="MetaAction.AttackMove"/> and <see cref="MetaAction.Guard"/> stay unbound: in the original they are control-bar hotkeys.
    /// </summary>
    public HotkeyMap WithEngineDefaults()
    {
        var added = new List<HotkeyEntry>(Entries);
        void Add(MetaAction action, KeyCode key)
        {
            if (added.All(entry => entry.Action != action))
                added.Add(new HotkeyEntry(action, new KeyBinding(key, KeyModifiers.None), HotkeyContexts.Game, "INTERFACE", string.Empty, string.Empty, HotkeySource.Engine));
        }
        Add(MetaAction.CameraScrollUp, KeyCode.Up);
        Add(MetaAction.CameraScrollDown, KeyCode.Down);
        Add(MetaAction.CameraScrollLeft, KeyCode.Left);
        Add(MetaAction.CameraScrollRight, KeyCode.Right);
        return new HotkeyMap([..added], Issues, Compatibility);
    }

    /// <summary>Binds <paramref name="action"/> to <paramref name="binding"/> (usable in the game unless it already had contexts); conflicts are recomputed, never resolved silently.</summary>
    public HotkeyMap Rebind(MetaAction action, KeyBinding binding)
    {
        var entries = Entries.Where(entry => entry.Action != action).ToList();
        var old = TryGetEntry(action, out var existing) ? existing : null;
        entries.Add(old is null
            ? new HotkeyEntry(action, binding, HotkeyContexts.Game, string.Empty, string.Empty, string.Empty, HotkeySource.User)
            : old with { Binding = binding, Source = HotkeySource.User });
        return new HotkeyMap([..entries], Issues, Compatibility);
    }

    public HotkeyMap Unbind(MetaAction action) => new([..Entries.Where(entry => entry.Action != action)], Issues, Compatibility);

    /// <summary>The options-screen representation: action name to <see cref="KeyBinding.ToString"/>, for every action (unbound ones as <see cref="Unbound"/>).</summary>
    public ImmutableSortedDictionary<string, string> ToSettings()
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var action in Enum.GetValues<MetaAction>())
            builder[MetaActionNames.NameOf(action)] = TryGetEntry(action, out var entry) ? entry.Binding.ToString() : Unbound;
        return builder.ToImmutable();
    }

    /// <summary>Applies saved settings on top of this map; unknown actions and unparsable values are reported and leave the binding unchanged.</summary>
    public HotkeyMap ApplySettings(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var map = this;
        var issues = ImmutableArray.CreateBuilder<HotkeyIssue>();
        foreach (var (name, value) in settings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!MetaActionNames.TryParse(name, out var action)) { issues.Add(new HotkeyIssue(HotkeyIssueKind.UnknownAction, name, "unknown action in settings")); continue; }
            if (value.Trim().Equals(Unbound, StringComparison.OrdinalIgnoreCase))
            {
                if (map.TryGetEntry(action, out _)) map = map.Unbind(action);
                continue;
            }
            if (!KeyBinding.TryParse(value, out var binding)) { issues.Add(new HotkeyIssue(HotkeyIssueKind.BadSettingValue, name, value)); continue; }
            if (!map.TryGetEntry(action, out var entry) || entry.Binding != binding) map = map.Rebind(action, binding);
        }
        return new HotkeyMap(map.Entries, [..Issues, ..issues], Compatibility);
    }

    private static ImmutableArray<HotkeyConflict> FindConflicts(ImmutableArray<HotkeyEntry> entries)
    {
        var conflicts = ImmutableArray.CreateBuilder<HotkeyConflict>();
        foreach (var group in entries.GroupBy(entry => entry.Binding).OrderBy(group => group.Key.Key).ThenBy(group => group.Key.Modifiers).ThenBy(group => group.Key.Transition))
        {
            var members = group.ToList();
            var clashing = new SortedSet<MetaAction>();
            for (var a = 0; a < members.Count; a++)
                for (var b = a + 1; b < members.Count; b++)
                    if ((members[a].UsableIn & members[b].UsableIn) != 0)
                    {
                        clashing.Add(members[a].Action);
                        clashing.Add(members[b].Action);
                    }
            if (clashing.Count > 0) conflicts.Add(new HotkeyConflict(group.Key, [..clashing]));
        }
        return conflicts.ToImmutable();
    }
}

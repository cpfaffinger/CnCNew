using System.Collections.Frozen;
using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Input;

/// <summary><c>GUICommandType</c> (<c>ControlBar.h</c>, INI names <c>TheGuiCommandNames</c>); the prisoner commands exist only with <c>ALLOW_SURRENDER</c> and are left out.</summary>
public enum GuiCommandKind
{
    None,
    DozerConstruct, DozerConstructCancel, UnitBuild, CancelUnitBuild, PlayerUpgrade, ObjectUpgrade, CancelUpgrade,
    AttackMove, Guard, GuardWithoutPursuit, GuardFlyingUnitsOnly, Stop, Waypoints,
    ExitContainer, Evacuate, ExecuteRailedTransport, BeaconDelete, SetRallyPoint, Sell,
    FireWeapon, SpecialPower, PurchaseScience, HackInternet, ToggleOvercharge, CombatDrop, SwitchWeapon,
    HijackVehicle, ConvertToCarbomb, SabotageBuilding, PlaceBeacon,
    SpecialPowerFromShortcut, SpecialPowerConstruct, SpecialPowerConstructFromShortcut, SelectAllUnitsOfType,
}

/// <summary><c>CommandOption</c> bits (<c>ControlBar.h</c>, INI names <c>TheCommandOptionNames</c>).</summary>
[Flags]
public enum CommandOptions
{
    None = 0,
    NeedTargetEnemyObject = 0x1,
    NeedTargetNeutralObject = 0x2,
    NeedTargetAllyObject = 0x4,
    AllowShrubberyTarget = 0x10,
    NeedTargetPos = 0x20,
    NeedUpgrade = 0x40,
    NeedSpecialPowerScience = 0x80,
    OkForMultiSelect = 0x100,
    ContextModeCommand = 0x200,
    CheckLike = 0x400,
    AllowMineTarget = 0x800,
    AttackObjectsPosition = 0x1000,
    OptionOne = 0x2000,
    OptionTwo = 0x4000,
    OptionThree = 0x8000,
    NotQueueable = 0x10000,
    SingleUseCommand = 0x20000,
    ScriptOnly = 0x80000,
    IgnoresUnderpowered = 0x100000,
    UsesMineClearingWeaponset = 0x200000,
    CanUseWaypoints = 0x400000,
    MustBeStopped = 0x800000,

    /// <summary><c>COMMAND_OPTION_NEED_OBJECT_TARGET</c>.</summary>
    NeedObjectTarget = NeedTargetEnemyObject | NeedTargetNeutralObject | NeedTargetAllyObject,
    /// <summary><c>COMMAND_OPTION_NEED_TARGET</c>: the button enters a targeting mode instead of acting at once.</summary>
    NeedTarget = NeedObjectTarget | NeedTargetPos | ContextModeCommand,
}

/// <summary>One <c>CommandButton</c> definition with the fields the input layer uses (<c>ControlBar.cpp</c> <c>s_commandButtonFieldParseTable</c>).</summary>
public sealed record CommandButtonInfo(
    string Name,
    GuiCommandKind Kind,
    CommandOptions Options,
    string Object,
    string Upgrade,
    string SpecialPower,
    ImmutableArray<string> Sciences,
    string WeaponSlot,
    string TextLabel,
    string DescriptLabel,
    string ConflictingLabel,
    string ButtonImage,
    string BorderType,
    string CursorName,
    string InvalidCursorName,
    string RadiusCursorType)
{
    public bool Has(CommandOptions option) => (Options & option) == option;
    public bool NeedsTarget => (Options & CommandOptions.NeedTarget) != 0;
}

/// <summary>A <c>CommandSet</c>: <see cref="MaxSlots"/> slots (<c>MAX_COMMANDS_PER_SET</c>), null where the set names no button.</summary>
public sealed record CommandSetInfo(string Name, ImmutableArray<CommandButtonInfo?> Slots)
{
    public const int MaxSlots = 18;
}

public sealed record CommandButtonIssue(string Definition, string Detail);

/// <summary>
/// The typed view of every <c>CommandButton</c> and <c>CommandSet</c> of a compiled definition graph, plus each object's own <c>CommandSet</c> field.
/// The compiler keeps these definitions with untyped field text; unknown command kinds, option names, fields and button references are
/// collected in <see cref="Issues"/> (the button is kept with <see cref="GuiCommandKind.None"/> resp. the slot left empty), never guessed.
/// </summary>
public sealed class CommandButtonCatalog
{
    private static readonly FrozenDictionary<string, GuiCommandKind> KindNames = new Dictionary<string, GuiCommandKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["NONE"] = GuiCommandKind.None, ["DOZER_CONSTRUCT"] = GuiCommandKind.DozerConstruct, ["DOZER_CONSTRUCT_CANCEL"] = GuiCommandKind.DozerConstructCancel,
        ["UNIT_BUILD"] = GuiCommandKind.UnitBuild, ["CANCEL_UNIT_BUILD"] = GuiCommandKind.CancelUnitBuild, ["PLAYER_UPGRADE"] = GuiCommandKind.PlayerUpgrade,
        ["OBJECT_UPGRADE"] = GuiCommandKind.ObjectUpgrade, ["CANCEL_UPGRADE"] = GuiCommandKind.CancelUpgrade, ["ATTACK_MOVE"] = GuiCommandKind.AttackMove,
        ["GUARD"] = GuiCommandKind.Guard, ["GUARD_WITHOUT_PURSUIT"] = GuiCommandKind.GuardWithoutPursuit, ["GUARD_FLYING_UNITS_ONLY"] = GuiCommandKind.GuardFlyingUnitsOnly,
        ["STOP"] = GuiCommandKind.Stop, ["WAYPOINTS"] = GuiCommandKind.Waypoints, ["EXIT_CONTAINER"] = GuiCommandKind.ExitContainer, ["EVACUATE"] = GuiCommandKind.Evacuate,
        ["EXECUTE_RAILED_TRANSPORT"] = GuiCommandKind.ExecuteRailedTransport, ["BEACON_DELETE"] = GuiCommandKind.BeaconDelete, ["SET_RALLY_POINT"] = GuiCommandKind.SetRallyPoint,
        ["SELL"] = GuiCommandKind.Sell, ["FIRE_WEAPON"] = GuiCommandKind.FireWeapon, ["SPECIAL_POWER"] = GuiCommandKind.SpecialPower,
        ["PURCHASE_SCIENCE"] = GuiCommandKind.PurchaseScience, ["HACK_INTERNET"] = GuiCommandKind.HackInternet, ["TOGGLE_OVERCHARGE"] = GuiCommandKind.ToggleOvercharge,
        ["COMBATDROP"] = GuiCommandKind.CombatDrop, ["SWITCH_WEAPON"] = GuiCommandKind.SwitchWeapon, ["HIJACK_VEHICLE"] = GuiCommandKind.HijackVehicle,
        ["CONVERT_TO_CARBOMB"] = GuiCommandKind.ConvertToCarbomb, ["SABOTAGE_BUILDING"] = GuiCommandKind.SabotageBuilding, ["PLACE_BEACON"] = GuiCommandKind.PlaceBeacon,
        ["SPECIAL_POWER_FROM_SHORTCUT"] = GuiCommandKind.SpecialPowerFromShortcut, ["SPECIAL_POWER_CONSTRUCT"] = GuiCommandKind.SpecialPowerConstruct,
        ["SPECIAL_POWER_CONSTRUCT_FROM_SHORTCUT"] = GuiCommandKind.SpecialPowerConstructFromShortcut, ["SELECT_ALL_UNITS_OF_TYPE"] = GuiCommandKind.SelectAllUnitsOfType,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, CommandOptions> OptionNames = new Dictionary<string, CommandOptions>(StringComparer.OrdinalIgnoreCase)
    {
        ["NEED_TARGET_ENEMY_OBJECT"] = CommandOptions.NeedTargetEnemyObject, ["NEED_TARGET_NEUTRAL_OBJECT"] = CommandOptions.NeedTargetNeutralObject,
        ["NEED_TARGET_ALLY_OBJECT"] = CommandOptions.NeedTargetAllyObject, ["ALLOW_SHRUBBERY_TARGET"] = CommandOptions.AllowShrubberyTarget,
        ["NEED_TARGET_POS"] = CommandOptions.NeedTargetPos, ["NEED_UPGRADE"] = CommandOptions.NeedUpgrade, ["NEED_SPECIAL_POWER_SCIENCE"] = CommandOptions.NeedSpecialPowerScience,
        ["OK_FOR_MULTI_SELECT"] = CommandOptions.OkForMultiSelect, ["CONTEXTMODE_COMMAND"] = CommandOptions.ContextModeCommand, ["CHECK_LIKE"] = CommandOptions.CheckLike,
        ["ALLOW_MINE_TARGET"] = CommandOptions.AllowMineTarget, ["ATTACK_OBJECTS_POSITION"] = CommandOptions.AttackObjectsPosition,
        ["OPTION_ONE"] = CommandOptions.OptionOne, ["OPTION_TWO"] = CommandOptions.OptionTwo, ["OPTION_THREE"] = CommandOptions.OptionThree,
        ["NOT_QUEUEABLE"] = CommandOptions.NotQueueable, ["SINGLE_USE_COMMAND"] = CommandOptions.SingleUseCommand, ["SCRIPT_ONLY"] = CommandOptions.ScriptOnly,
        ["IGNORES_UNDERPOWERED"] = CommandOptions.IgnoresUnderpowered, ["USES_MINE_CLEARING_WEAPONSET"] = CommandOptions.UsesMineClearingWeaponset,
        ["CAN_USE_WAYPOINTS"] = CommandOptions.CanUseWaypoints, ["MUST_BE_STOPPED"] = CommandOptions.MustBeStopped,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> KnownFields = new[]
    {
        "Command", "Options", "Object", "Upgrade", "WeaponSlot", "MaxShotsToFire", "Science", "SpecialPower", "TextLabel", "DescriptLabel",
        "PurchasedLabel", "ConflictingLabel", "ButtonImage", "CursorName", "InvalidCursorName", "ButtonBorderType", "RadiusCursorType", "UnitSpecificSound",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly FrozenDictionary<string, CommandButtonInfo> _buttons;
    private readonly FrozenDictionary<string, CommandSetInfo> _sets;
    private readonly FrozenDictionary<string, string> _objectSets;

    private CommandButtonCatalog(Dictionary<string, CommandButtonInfo> buttons, Dictionary<string, CommandSetInfo> sets, Dictionary<string, string> objectSets,
        ImmutableArray<CommandButtonIssue> issues, int slotsBeyondGrid)
    {
        _buttons = buttons.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _sets = sets.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _objectSets = objectSets.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        Issues = issues;
        SlotsBeyondGrid = slotsBeyondGrid;
    }

    public int ButtonCount => _buttons.Count;
    public int SetCount => _sets.Count;
    public ImmutableArray<CommandButtonIssue> Issues { get; }

    /// <summary>Buttons without a <c>Command</c> field (the reference default <see cref="GuiCommandKind.None"/>).</summary>
    public int ButtonsWithoutCommand => _buttons.Values.Count(button => button.Kind == GuiCommandKind.None);

    /// <summary>Set entries in slots 15..18, which the retail control bar (14 <c>ButtonCommand</c> windows) never shows.</summary>
    public int SlotsBeyondGrid { get; }

    public bool TryGetButton(string name, out CommandButtonInfo button) => _buttons.TryGetValue(name, out button!);
    public bool TryGetSet(string name, out CommandSetInfo set) => _sets.TryGetValue(name, out set!);

    /// <summary>The object's own <c>CommandSet</c> field (before any <c>CommandSetUpgrade</c>).</summary>
    public bool TryGetObjectSet(string objectName, out string set) => _objectSets.TryGetValue(objectName, out set!);

    public static CommandButtonCatalog Build(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var issues = new List<CommandButtonIssue>();
        var buttons = new Dictionary<string, CommandButtonInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in graph.Definitions.OfType<CommandButtonDefinition>().OrderBy(value => value.Id.Name, StringComparer.Ordinal))
            buttons[definition.OriginalName] = ParseButton(definition, issues);

        var sets = new Dictionary<string, CommandSetInfo>(StringComparer.OrdinalIgnoreCase);
        var beyondGrid = 0;
        foreach (var definition in graph.Definitions.OfType<CommandSetDefinition>().OrderBy(value => value.Id.Name, StringComparer.Ordinal))
        {
            var slots = new CommandButtonInfo?[CommandSetInfo.MaxSlots];
            foreach (var field in definition.Fields)
            {
                if (!int.TryParse(field.Name, out var slot) || slot < 1 || slot > CommandSetInfo.MaxSlots)
                {
                    issues.Add(new CommandButtonIssue(definition.OriginalName, $"slot key '{field.Name}' is not 1..{CommandSetInfo.MaxSlots}"));
                    continue;
                }
                var name = field.Value.OriginalText.Trim();
                if (name.Equals("NONE", StringComparison.OrdinalIgnoreCase) || name.Length == 0) continue;
                if (!buttons.TryGetValue(name, out var button))
                {
                    issues.Add(new CommandButtonIssue(definition.OriginalName, $"slot {slot} names unknown button '{name}'"));
                    continue;
                }
                slots[slot - 1] = button;
                if (slot > CommandBarModel.GridSlots) beyondGrid++;
            }
            sets[definition.OriginalName] = new CommandSetInfo(definition.OriginalName, [..slots]);
        }

        var objectSets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>())
            if (definition.Fields.LastOrDefault(field => field.Name.Equals("CommandSet", StringComparison.OrdinalIgnoreCase)) is { } set)
                objectSets[definition.OriginalName] = set.Value.OriginalText.Trim();
        return new CommandButtonCatalog(buttons, sets, objectSets, [..issues], beyondGrid);
    }

    private static CommandButtonInfo ParseButton(CommandButtonDefinition definition, List<CommandButtonIssue> issues)
    {
        string Text(string name) => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim() ?? string.Empty;
        foreach (var field in definition.Fields)
            if (!KnownFields.Contains(field.Name)) issues.Add(new CommandButtonIssue(definition.OriginalName, $"unknown field '{field.Name}'"));
        var commandText = Text("Command");
        // CommandButton::CommandButton starts at GUI_COMMAND_NONE: the NonCommand_* buttons (tooltip-only UI buttons) name no command.
        if (commandText.Length == 0) commandText = "NONE";
        if (!KindNames.TryGetValue(commandText, out var kind))
        {
            issues.Add(new CommandButtonIssue(definition.OriginalName, $"unknown Command '{commandText}'"));
            kind = GuiCommandKind.None;
        }
        var options = CommandOptions.None;
        foreach (var token in Text("Options").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (OptionNames.TryGetValue(token, out var option)) options |= option;
            else issues.Add(new CommandButtonIssue(definition.OriginalName, $"unknown option '{token}'"));
        }
        ImmutableArray<string> sciences = [..Text("Science").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Where(value => !value.Equals("NONE", StringComparison.OrdinalIgnoreCase))];
        return new CommandButtonInfo(definition.OriginalName, kind, options, Text("Object"), Text("Upgrade"), Text("SpecialPower"), sciences,
            Text("WeaponSlot") is { Length: > 0 } slot ? slot : "PRIMARY", Text("TextLabel"), Text("DescriptLabel"), Text("ConflictingLabel"), Text("ButtonImage"),
            Text("ButtonBorderType") is { Length: > 0 } border ? border : "NONE", Text("CursorName"), Text("InvalidCursorName"), Text("RadiusCursorType"));
    }

    /// <summary>
    /// <c>HotKeyManager::searchHotKey</c>: the character after the first <c>&amp;</c> of the translated <see cref="CommandButtonInfo.TextLabel"/>, lowercased;
    /// null without a translation or marker. The text comes from the caller (the string table lives in the UI layer).
    /// </summary>
    public static char? HotkeyOf(CommandButtonInfo button, Func<string, string?> text)
    {
        if (button.TextLabel.Length == 0 || text(button.TextLabel) is not { } label) return null;
        var marker = label.IndexOf('&');
        return marker >= 0 && marker + 1 < label.Length ? char.ToLowerInvariant(label[marker + 1]) : null;
    }
}

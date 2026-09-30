namespace CnCNew.Ui.Runtime;

/// <summary>
/// Window messages the runtime emits. Each kind names the original message it stands for
/// (<c>Gadget.h</c>, <c>GameWindow.h</c>); see <c>docs/UI-WND.md</c> for the list of messages that are not emitted.
/// </summary>
public enum UiEventKind
{
    /// <summary><c>GBM_MOUSE_ENTERING</c>: the pointer entered a <c>MOUSETRACK</c> gadget.</summary>
    ButtonMouseEntering,
    /// <summary><c>GBM_MOUSE_LEAVING</c>.</summary>
    ButtonMouseLeaving,
    /// <summary><c>GBM_SELECTED</c>: push button clicked, check box toggled or radio button selected (Value = 1 when checked).</summary>
    ButtonSelected,
    /// <summary><c>GBM_SELECTED_RIGHT</c>: right click on a push button with <c>RIGHT_CLICK</c> status.</summary>
    ButtonSelectedRight,
    /// <summary><c>GSM_SLIDER_TRACK</c>: slider value changed (Value = new value).</summary>
    SliderTrack,
    /// <summary><c>GSM_SLIDER_DONE</c>: slider drag finished (Value = final value).</summary>
    SliderDone,
    /// <summary><c>GLM_SELECTED</c>: list box selection changed (Value = row, -1 for none).</summary>
    ListSelected,
    /// <summary><c>GLM_DOUBLE_CLICKED</c> (Value = row).</summary>
    ListDoubleClicked,
    /// <summary><c>GLM_RIGHT_CLICKED</c> (Value = row, -1 below the last row).</summary>
    ListRightClicked,
    /// <summary><c>GCM_SELECTED</c>: combo box selection changed (Value = entry index).</summary>
    ComboSelected,
    /// <summary><c>GCM_UPDATE_TEXT</c>: the text of an editable combo box changed (Text = new text).</summary>
    ComboUpdateText,
    /// <summary><c>GEM_UPDATE_TEXT</c>: text entry content changed (Text = new text).</summary>
    EntryUpdateText,
    /// <summary><c>GEM_EDIT_DONE</c>: Enter in a text entry (Text = content).</summary>
    EntryEditDone,
    /// <summary><c>GGM_FOCUS_CHANGE</c>: Value = 1 when the source gained focus, 0 when it lost it.</summary>
    FocusChange,
    /// <summary>A key no gadget consumed (<c>GWM_CHAR</c> passed to the input callbacks; Value = <see cref="UiKey"/>).</summary>
    Key,
}

/// <summary>
/// One window message. <see cref="Source"/> is the decorated name of the emitting window (<c>File.wnd:Name</c>);
/// <see cref="Target"/> is the window whose callback received it (set on delivery).
/// </summary>
public sealed record UiEvent(UiEventKind Kind, string Source, int Value = 0, string? Text = null, string? Target = null);

public enum UiDeliveryOutcome
{
    /// <summary>A registered handler received the event.</summary>
    Handled,
    /// <summary>The receiving window has no callback (<c>[None]</c> or a default/gadget system function that ignores it).</summary>
    NoCallback,
    /// <summary><c>PassSelectedButtonsToParentSystem</c> stopped a kind it does not forward.</summary>
    Filtered,
    /// <summary>The receiving callback name has no registered handler; counted in <see cref="UiWindowTree.UnregisteredCallbacks"/>.</summary>
    Unregistered,
    /// <summary><c>GameWinBlockInput</c> consumed a key.</summary>
    Blocked,
    /// <summary>No window with an input callback was found for a key.</summary>
    NoReceiver,
}

public sealed record UiDelivery(UiEvent Event, string? Callback, UiDeliveryOutcome Outcome);

public enum UiMouseButton
{
    Left,
    Right,
    Middle,
}

public enum UiKey
{
    Tab,
    Enter,
    Escape,
    Space,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    PageUp,
    PageDown,
    Backspace,
    Delete,
    Other,
}

[Flags]
public enum UiModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

/// <summary>The visible tooltip: text (resolved or the raw label) and the pointer position where it appeared.</summary>
public sealed record UiTooltip(string WindowName, string Text, string? LabelKey, int X, int Y);

/// <summary>Callback names the runtime implements itself (the rest must be registered by the shell).</summary>
public static class UiBuiltinCallbacks
{
    public const string PassMessagesToParent = "PassMessagesToParentSystem";
    public const string PassSelectedButtonsToParent = "PassSelectedButtonsToParentSystem";
    public const string DefaultSystem = "GameWinDefaultSystem";
    public const string BlockInput = "GameWinBlockInput";
    public const string DefaultInput = "GameWinDefaultInput";
    public const string DefaultTooltip = "GameWinDefaultTooltip";
    public const string NoDraw = "W3DNoDraw";
    public const string DefaultDraw = "GameWinDefaultDraw";
    public const string W3DDefaultDraw = "W3DGameWinDefaultDraw";
    public const string PushButtonImageDraw = "W3DGadgetPushButtonImageDraw";

    /// <summary>Gadget system/input functions named in a WND file stand for the gadget's own behaviour, which ignores routed messages.</summary>
    public static bool IsGadgetFunction(string name)
        => name.StartsWith("Gadget", StringComparison.Ordinal) && (name.EndsWith("System", StringComparison.Ordinal) || name.EndsWith("Input", StringComparison.Ordinal));
}

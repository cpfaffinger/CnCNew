using System.Collections.Immutable;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Hud;

/// <summary>
/// The message window of <c>INGAME_POPUP_MESSAGE</c>: <c>InGamePopupMessage.wnd</c> as a window tree of its own, laid out the way <c>InGamePopupMessageInit</c> does it (InGamePopupMessage.cpp) -
/// the parent at the given percentage of the screen and as wide as the script said, the text window 2 pixels inside it and as high as the text wrapped 14 pixels short of the width needs (+ 7), the OK
/// button 2 pixels from the right edge below the text, the parent as high as all of it. A popup that pauses the game is modal (every event but the window's own is swallowed); one that does not lets
/// the world and HUD go on, except for the events over the window. The OK button, Enter and Escape (on key up) close it (<c>InGamePopupMessageSystem</c>/<c>Input</c>).
/// <para>
/// The text color is the layout's own (254 gray), not <c>PopupMessageColor</c> (<c>winSetEnabledTextColors</c>): the tree has no per-window text color override, and the retail values differ by one step.
/// </para>
/// </summary>
public sealed class MissionPopupWindow
{
    private const string ParentName = "InGamePopupMessage.wnd:InGamePopupMessageParent";
    private const string TextName = "InGamePopupMessage.wnd:StaticTextMessage";
    private const string OkName = "InGamePopupMessage.wnd:ButtonOk";

    private readonly UiWindowTree _tree;
    private readonly UiWindow _parent;
    private readonly UiWindow _text;
    private readonly UiWindow _ok;
    private readonly UiScaleOptions _options;
    private readonly IUiTextMeasure _measure;
    private PopupMessage? _message;
    private UiViewport _laidOutFor;

    public MissionPopupWindow(HudAssets assets, IUiTextMeasure measure, UiScaleOptions options, UiViewport viewport, CnCNew.Ui.Skins.UiSkin? skin = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(options);
        _measure = measure;
        _options = options;
        _tree = new UiWindowTree(viewport, options, assets.BuildTreeServices(measure));
        var roots = _tree.AddLayout((skin ?? CnCNew.Ui.Skins.UiSkin.Original).Derive(assets.InGamePopup, assets.HeaderTemplates));
        _parent = roots.SingleOrDefault(window => window.Name == ParentName) ?? throw new InvalidDataException("InGamePopupMessage.wnd has no InGamePopupMessageParent window.");
        _text = _tree.FindByName(TextName) ?? throw new InvalidDataException("InGamePopupMessage.wnd has no StaticTextMessage window.");
        _ok = _tree.FindByName(OkName) ?? throw new InvalidDataException("InGamePopupMessage.wnd has no ButtonOk window.");
        _tree.Register("InGamePopupMessageSystem", OnSystem);
        _parent.SetHidden(true);
        _laidOutFor = viewport;
    }

    /// <summary>The popup on screen, or <see langword="null"/>.</summary>
    public PopupMessage? Message => _message;

    public bool IsOpen => _message is not null;

    /// <summary>Raised when the player closed the popup (OK, Enter, Escape).</summary>
    public event Action? Closed;

    /// <summary>The rectangle of the window on screen (empty when closed), for hit tests.</summary>
    public UiRect Rect => _message is null ? default : _parent.Rect;

    /// <summary>The rectangle of the OK button on screen (empty when closed).</summary>
    public UiRect OkRect => _message is null ? default : _ok.Rect;

    /// <summary><c>InGameUI::popupMessage</c> -> <c>InGamePopupMessageInit</c>: shows <paramref name="message"/> for a screen of <paramref name="viewport"/>.</summary>
    public void Show(PopupMessage message, UiViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(message);
        _message = message;
        _text.SetText(message.Text);
        _parent.SetHidden(false);
        Layout(viewport);
    }

    /// <summary>Removes the window without raising <see cref="Closed"/> (the script side already dropped it).</summary>
    public void Hide()
    {
        _message = null;
        _parent.SetHidden(true);
    }

    private void Layout(UiViewport viewport)
    {
        if (_message is not { } message) return;
        if (viewport != _tree.Viewport) _tree.SetViewport(viewport, _options);
        _laidOutFor = viewport;
        // The button keeps the size the layout gives it at this screen size; the other rectangles are placed by the code, in pixels (winSetPosition/winSetSize).
        _parent.SetScreenRect(null);
        _text.SetScreenRect(null);
        _ok.SetScreenRect(null);
        var okWidth = _ok.Rect.Width;
        var okHeight = _ok.Rect.Height;
        var x = (int)(viewport.Width * (message.XPercent / 100f));
        var y = (int)(viewport.Height * (message.YPercent / 100f));
        var textHeight = UiSentenceLayout.Layout(message.Text, _text.Font, _measure, message.Width - 14, false).Height;
        _parent.SetScreenRect(new UiRect(x, y, message.Width, textHeight + 7 + 2 + 2 + okHeight + 2));
        _text.SetScreenRect(new UiRect(x + 2, y + 2, message.Width - 4, textHeight + 7));
        _ok.SetScreenRect(new UiRect(x + message.Width - okWidth - 2, y + textHeight + 7 + 2 + 2, okWidth, okHeight));
    }

    /// <summary>The window's draw commands for a screen of <paramref name="width"/> x <paramref name="height"/> (none while closed).</summary>
    public ImmutableArray<UiDrawCommand> BuildCommands(int width, int height)
    {
        if (_message is null) return [];
        var viewport = new UiViewport(width, height);
        if (viewport != _laidOutFor) Layout(viewport);
        return _tree.BuildDrawList().Commands;
    }

    /// <summary>Advances the window's own clock (button hilite timing); the host calls it once per frame.</summary>
    public void Advance(long milliseconds) => _tree.AdvanceTo(milliseconds);

    /// <summary>
    /// Offers an input event to the window; returns <see langword="true"/> when it took it (the host then does not pass it on). Enter and Escape close the popup on key up (both key states are taken,
    /// as in the original), a pointer event over the window goes to the window, and while the popup pauses the game everything else is swallowed too.
    /// </summary>
    public bool Process(InputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        if (_message is not { } message) return false;
        switch (inputEvent)
        {
            case KeyEvent { Key: KeyCode.Enter or KeyCode.Escape or KeyCode.KeypadEnter } key:
                if (!key.IsDown) Close();
                return true;
            case MouseMoveEvent move:
            {
                var over = _tree.MouseMove((int)MathF.Round(move.Position.X), (int)MathF.Round(move.Position.Y));
                return over || message.Pause;
            }
            case MouseButtonEvent button:
            {
                var x = (int)MathF.Round(button.Position.X);
                var y = (int)MathF.Round(button.Position.Y);
                var uiButton = button.Button switch { MouseButton.Left => UiMouseButton.Left, MouseButton.Right => UiMouseButton.Right, _ => UiMouseButton.Middle };
                if (button.IsDown)
                {
                    if (_tree.InputTarget(x, y) is null) return message.Pause;
                    _tree.MouseDown(x, y, uiButton);
                    _tree.Dispatch();
                    return true;
                }
                if (_tree.Grabbed is null) return message.Pause;
                _tree.MouseUp(x, y, uiButton);
                _tree.Dispatch();
                return true;
            }
            default:
                return message.Pause;
        }
    }

    private void Close()
    {
        if (_message is null) return;
        Hide();
        Closed?.Invoke();
    }

    private void OnSystem(UiWindowTree tree, UiEvent uiEvent)
    {
        if (uiEvent.Kind == UiEventKind.ButtonSelected && uiEvent.Source == OkName) Close();
    }
}

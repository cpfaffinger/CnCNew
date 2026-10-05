using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell.Screens;

public enum ShellMessageBoxButtons { OkOnly, YesNo, YesNoCancel }

/// <summary>
/// Shared implementation of <c>MessageBox.wnd</c> and <c>QuitMessageBox.wnd</c> (identical inner layout; the root's
/// <c>SYSTEMCALLBACK</c> differs). Both ship every button (<c>ButtonYes/No/Cancel/Ok</c>) <c>Hidden</c> and a
/// placeholder <c>GUI:Munkee</c> title/message; the caller supplies the real labels and which buttons apply. Neither
/// layout declares an <c>INPUTCALLBACK</c> anywhere (<c>in=</c> is blank on every window, confirmed against the shipped
/// file), so Escape is not wired here either — a button click is the only way out, matching the shipped data.
/// </summary>
public sealed class MessageBoxScreen : ShellScreen
{
    /// <summary>A message box is a modal popup (<c>MessageBoxOk</c>/<c>QuitMessageBoxYesNo</c> create their own layout), not a shell push: the screen below stays visible behind it.</summary>
    public override bool HidesScreensBelow => false;

    private readonly Action _onAccept;
    private readonly Action? _onDecline;

    public MessageBoxScreen(GameShell shell, string layoutName, string titleLabel, string messageLabel,
        ShellMessageBoxButtons buttons, Action onAccept, Action? onDecline = null) : this(shell, layoutName, titleLabel, messageLabel, plainMessage: false, buttons, onAccept, onDecline)
    {
    }

    /// <summary>Same as the main constructor, but <paramref name="plainMessage"/> shows <paramref name="titleLabel"/>/
    /// <paramref name="messageLabel"/> as already-resolved text instead of CSF labels (for messages built at runtime, e.g. a
    /// <c>SaveGameException</c> reason, which are never label keys and would otherwise count as unresolved labels).</summary>
    public MessageBoxScreen(GameShell shell, string layoutName, string titleLabel, string messageLabel, bool plainMessage,
        ShellMessageBoxButtons buttons, Action onAccept, Action? onDecline = null) : base(shell, layoutName)
    {
        _onAccept = onAccept;
        _onDecline = onDecline;
        Tree.Register(layoutName == ShellLayoutNames.QuitMessageBox ? "QuitMessageBoxSystem" : "MessageBoxSystem", OnSystem);
        if (plainMessage) { W("StaticTextTitle").SetText(titleLabel); W("StaticTextMessage").SetText(messageLabel); }
        else { W("StaticTextTitle").SetTextLabel(titleLabel); W("StaticTextMessage").SetTextLabel(messageLabel); }
        W("ButtonOk").SetHidden(buttons != ShellMessageBoxButtons.OkOnly);
        W("ButtonYes").SetHidden(buttons == ShellMessageBoxButtons.OkOnly);
        W("ButtonNo").SetHidden(buttons == ShellMessageBoxButtons.OkOnly);
        W("ButtonCancel").SetHidden(buttons != ShellMessageBoxButtons.YesNoCancel);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        var name = e.Source[(LayoutName.Length + 5)..]; // strip "<LayoutName>.wnd:"
        switch (name)
        {
            case "ButtonOk" or "ButtonYes": Shell.PlaySound(ShellUiSounds.ButtonClick); _onAccept(); break;
            case "ButtonNo" or "ButtonCancel": Shell.PlaySound(ShellUiSounds.ButtonClick); _onDecline?.Invoke(); break;
        }
    }
}

using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell;

/// <summary>
/// One WND-driven screen: its own <see cref="UiWindowTree"/> (so a modal dialog gets its own focus order, never leaking
/// into the screen underneath), the callbacks it registers, and whatever per-screen state the shell needs (working
/// settings, skirmish choices, ...). <see cref="GameShell"/> keeps a stack of these; only the top one receives input.
/// </summary>
public abstract class ShellScreen
{
    protected ShellScreen(GameShell shell, string layoutName)
    {
        Shell = shell;
        LayoutName = layoutName;
        Tree = new UiWindowTree(shell.Viewport, shell.ScaleOptions, shell.TreeServices);
        Tree.AddLayout(shell.Layouts.Get(layoutName));
    }

    protected GameShell Shell { get; }
    public string LayoutName { get; }
    public UiWindowTree Tree { get; }

    /// <summary>Looks up a window of this screen's own layout by its undecorated name; throws if the shipped WND file does not have it (never a silent null).</summary>
    protected UiWindow W(string name) => Tree.FindByName($"{LayoutName}.wnd:{name}")
        ?? throw new InvalidOperationException($"Window '{name}' does not exist in {LayoutName}.wnd.");

    protected UiWindow? TryW(string name) => Tree.FindByName($"{LayoutName}.wnd:{name}");

    /// <summary>Resolves a label through the shell's text source, falling back to the label key itself (never thrown away silently: unresolved labels are also counted when drawn).</summary>
    protected string Text(string label) => Shell.TextSource is { } source && source.TryResolve(label, out var text) ? text : label;

    public virtual void Update(double dtSeconds) { }

    public virtual UiDrawList BuildDrawList() => Tree.BuildDrawList();

    /// <summary>Called once when the screen leaves the stack (popped or replaced); default does nothing.</summary>
    public virtual void OnClosed() { }
}

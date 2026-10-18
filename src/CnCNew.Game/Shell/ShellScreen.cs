using CnCNew.Ui.Runtime;
using CnCNew.Ui.Transitions;

namespace CnCNew.Game.Shell;

/// <summary>
/// One WND-driven screen: its own <see cref="UiWindowTree"/> (so a modal dialog gets its own focus order, never leaking
/// into the screen underneath), the callbacks it registers, and whatever per-screen state the shell needs (working
/// settings, skirmish choices, ...). <see cref="GameShell"/> keeps a stack of these; only the top one receives input.
/// </summary>
public abstract class ShellScreen
{
    protected ShellScreen(GameShell shell, string layoutName) : this(shell, layoutName, shell.Layout(layoutName)) { }

    /// <summary>A screen on a layout the shell does not ship as a file of its own (made at run time from shipped ones, already in the shell's skin).</summary>
    protected ShellScreen(GameShell shell, string layoutName, CnCNew.Ui.Wnd.WndLayout layout)
    {
        Shell = shell;
        LayoutName = layoutName;
        Tree = new UiWindowTree(shell.Viewport, shell.ScaleOptions, shell.TreeServices);
        Tree.AddLayout(layout);
        if (shell.TransitionTable is { } table) Transitions = new WindowTransitionHandler(table, new UiTransitionHost(Tree, shell.PlaySound));
    }

    /// <summary>The longest stretch of time one <see cref="Update"/> may turn into transition frames. The original steps one frame per client frame;
    /// a stall (loading, a dragged window) must not play a whole sequence in a single call.</summary>
    public const double MaxTransitionStepSeconds = 0.1;

    protected GameShell Shell { get; }
    public string LayoutName { get; }

    /// <summary>
    /// Whether the screens below this one are shut down and hidden while it is open (<c>Shell::push</c>: the top screen runs its shutdown, which ends
    /// in <c>layout-&gt;hide(TRUE)</c>, before the new layout is created) or stay on screen behind it. Screens that the original opens by
    /// <c>Shell::push</c> or <c>Shell::pop</c> (skirmish setup, credits, challenge menu, load game, score screen) replace what lies below, so the
    /// shell draws only the topmost such screen and what stands above it. The options menu (<c>getOptionsLayout</c> + <c>hide(FALSE)</c>), the skirmish map
    /// picker (<c>winCreateLayout</c> + <c>hide(FALSE)</c>) and the message boxes are layered over the screen below it, which keeps running; they return <see langword="false"/>.
    /// </summary>
    public virtual bool HidesScreensBelow => true;
    public UiWindowTree Tree { get; }

    /// <summary>Looks up a window of this screen's own layout by its undecorated name; throws if the shipped WND file does not have it (never a silent null).</summary>
    protected UiWindow W(string name) => Tree.FindByName($"{LayoutName}.wnd:{name}")
        ?? throw new InvalidOperationException($"Window '{name}' does not exist in {LayoutName}.wnd.");

    /// <summary>Hides a window of this screen's layout (throws if the shipped file does not have it).</summary>
    internal void HideWindow(string name) => W(name).SetHidden(true);

    protected UiWindow? TryW(string name) => Tree.FindByName($"{LayoutName}.wnd:{name}");

    /// <summary>Resolves a label through the shell's text source, falling back to the label key itself (never thrown away silently: unresolved labels are also counted when drawn).</summary>
    protected string Text(string label) => Shell.TextSource is { } source && source.TryResolve(label, out var text) ? text : label;

    /// <summary>
    /// The window transitions of this screen (<c>TheTransitionHandler</c> of the original, one per screen here because every screen has its own
    /// window tree); <see langword="null"/> when the shell runs without animations (headless tests and golden hashes, <c>docs/GAME-SHELL.md</c> §11).
    /// </summary>
    public WindowTransitionHandler? Transitions { get; }

    /// <summary>The transition group played backwards when the screen is closed with <see cref="GameShell.CloseTop"/>; the screen is popped once it has finished (the original waits for <c>isAnimFinished</c> before <c>shutdownComplete</c>).</summary>
    protected virtual string? ExitTransitionGroup => null;

    /// <summary>Set while the exit transition runs; the shell passes no input to a closing screen.</summary>
    public bool IsClosing { get; private set; }

    /// <summary>Starts the exit transition if the screen has one and the shell animates; returns whether the pop has to wait.</summary>
    internal bool BeginExit()
    {
        if (Transitions is null || ExitTransitionGroup is not { } group) return false;
        if (IsClosing) return true;
        IsClosing = true;
        Transitions.Reverse(group);
        return true;
    }

    /// <summary>A transition runs or one is waiting to run; the host keeps drawing frames (and a test keeps stepping) until it is false.</summary>
    public virtual bool IsAnimating => Transitions is { IsSettled: false };

    /// <summary>The screen has nothing left to wait for: its exit transition (if any) is over.</summary>
    internal bool ExitFinished => IsClosing && (Transitions?.IsSettled ?? true);

    public virtual void Update(double dtSeconds) => Transitions?.Advance(Math.Min(dtSeconds, MaxTransitionStepSeconds));

    /// <summary>The screen on top of this one was closed and this one is the top again (the original re-runs the layout's init, e.g. <c>MainMenuInit</c>).</summary>
    public virtual void OnRevealed() { }

    public virtual UiDrawList BuildDrawList() => Tree.BuildDrawList();

    /// <summary>Called once when the screen leaves the stack (popped or replaced); default does nothing.</summary>
    public virtual void OnClosed() { }
}

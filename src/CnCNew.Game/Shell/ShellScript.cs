using System.Globalization;
using System.Numerics;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell;

/// <summary>
/// A person at the menus, as a file (docs/GAME-SHELL.md section 16; the client's <c>--shell-script FILE</c> and the tests of the screens): a sequence of steps that
/// each wait for what a person would wait for - a screen, a text, a button that can be pressed - and then do what a person would do: move the pointer onto a window
/// and click, type into a field, pick an entry of a box or a row of a list. Everything it does is an <see cref="InputEvent"/> handed to <see cref="GameShell.HandleInput"/>,
/// the same way the device's events go; it never calls a screen. Every step has a time limit in shell time (default <see cref="DefaultLimitSeconds"/>); a step whose
/// condition does not come true in time ends the script as failed, with what the shell showed instead. Unlike the frame-numbered <c>--input-script</c> of a running
/// match the steps are conditions, because what a server answers and when is not a frame number.
/// <para>
/// One step per line, <c>#</c> starts a comment, windows are named as in the layout (<c>LanLobbyMenu.wnd:ButtonHost</c>):
/// <c>wait screen NAME</c>, <c>wait phase NAME</c>, <c>wait enabled|disabled|shown|hidden WINDOW</c>, <c>wait text WINDOW contains|equals|lacks TEXT</c>,
/// <c>wait list WINDOW contains|lacks TEXT</c>, <c>wait combo WINDOW shows TEXT</c>, <c>wait seconds N</c>; the same with <c>expect</c> instead of <c>wait</c> must hold
/// at once; <c>click WINDOW</c> (waits until the window can be clicked), <c>type WINDOW TEXT</c> (replaces what the field holds; <c>""</c> empties it),
/// <c>enter WINDOW</c> (the Enter key in a field), <c>select WINDOW TEXT</c> (an entry of a combo box, by a part of its text), <c>pick WINDOW TEXT</c> (a row of a list),
/// <c>key KEYCODE</c>, <c>limit SECONDS</c> (the time limit of the steps that follow), <c>note TEXT</c>. <c>$NAME</c> in a text is replaced by the variable of that
/// name (<see cref="Variables"/>; the client fills them from <c>--shell-var NAME=VALUE</c>). The text of <c>contains</c> and <c>shows</c> may name alternatives,
/// separated by <c>|</c>: one of them is enough (who is back in a room first is not the script's to decide).
/// </para>
/// </summary>
public sealed class ShellScript
{
    public const double DefaultLimitSeconds = 20;

    private sealed record Step(int Line, string Text, string Verb, string[] Words, string Rest);

    private readonly List<Step> _steps = [];
    private readonly List<string> _log = [];
    private int _next;
    private double _waited;
    private double _limit = DefaultLimitSeconds;
    private double _clock;
    private long _events;

    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);
    public bool IsFinished => Failed || _next >= _steps.Count;
    public bool Failed { get; private set; }
    public int StepsDone => _next;
    public int StepCount => _steps.Count;
    public IReadOnlyList<string> Log => _log;
    /// <summary>What a match in progress tells the script (the tick, whether it is over); the host sets it while it owns a match, the shell knows nothing of it.</summary>
    public Func<string, string?>? MatchProbe { get; set; }
    /// <summary>Called with every line of the log as it is written: the client prints them as they happen, so that a check can wait for a step of another process.</summary>
    public Action<string>? Trace { get; set; }

    public static ShellScript Load(string path) => Parse(File.ReadAllLines(path), path);

    public static ShellScript Parse(IEnumerable<string> lines, string name = "script")
    {
        var script = new ShellScript();
        var number = 0;
        foreach (var raw in lines)
        {
            number++;
            var text = raw;
            var comment = text.IndexOf('#');
            while (comment > 0 && !char.IsWhiteSpace(text[comment - 1])) comment = text.IndexOf('#', comment + 1);
            if (comment >= 0) text = text[..comment];
            text = text.Trim();
            if (text.Length == 0) continue;
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var verb = words[0].ToLowerInvariant();
            if (verb is not ("wait" or "expect" or "click" or "type" or "enter" or "select" or "pick" or "key" or "limit" or "note"))
                throw new InvalidDataException($"{name}({number}): unknown step '{words[0]}'.");
            script._steps.Add(new Step(number, text, verb, words, text[words[0].Length..].Trim()));
        }
        return script;
    }

    /// <summary>One frame of the script: steps run until one has to wait. Call it once per shell update, before the update.</summary>
    public void Advance(GameShell shell, double seconds)
    {
        _clock += seconds * 1000;
        if (IsFinished) return;
        _waited += seconds;
        // A frame ends with the first step that produced input: the shell answers it in its update, as it answers a person.
        while (!IsFinished)
        {
            var step = _steps[_next];
            var outcome = Run(shell, step);
            if (outcome == Outcome.Wait)
            {
                if (_waited > _limit) Fail(shell, step, $"not within {_limit.ToString("0.#", CultureInfo.InvariantCulture)} s");
                return;
            }
            if (outcome == Outcome.Failed) return;
            Write($"shell-script: step {step.Line} ok after {_waited.ToString("0.00", CultureInfo.InvariantCulture)} s: {step.Text}");
            _next++;
            _waited = 0;
            if (outcome == Outcome.DoneWithInput) return;
        }
    }

    private enum Outcome { Done, DoneWithInput, Wait, Failed }

    private string Expand(string text)
    {
        foreach (var (name, value) in Variables.OrderByDescending(pair => pair.Key.Length)) text = text.Replace("$" + name, value, StringComparison.Ordinal);
        return text is "\"\"" ? string.Empty : text;
    }

    private Outcome Run(GameShell shell, Step step)
    {
        var words = step.Words;
        switch (step.Verb)
        {
            case "note":
                Write($"shell-script: note: {Expand(step.Rest)}");
                return Outcome.Done;
            case "limit":
                if (words.Length != 2 || !double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var limit) || limit <= 0) return Fail(shell, step, "expected 'limit SECONDS'");
                _limit = limit;
                return Outcome.Done;
            case "wait" or "expect":
            {
                if (words.Length < 3) return Fail(shell, step, "incomplete condition");
                var holds = Condition(shell, step, out var problem);
                if (problem is not null) return Fail(shell, step, problem);
                if (holds) return Outcome.Done;
                return step.Verb == "expect" ? Fail(shell, step, "does not hold") : Outcome.Wait;
            }
            case "key":
                if (words.Length != 2 || !Enum.TryParse<KeyCode>(words[1], true, out var key)) return Fail(shell, step, "expected 'key KEYCODE'");
                Send(shell, new KeyEvent(Now(), key, IsDown: true));
                Send(shell, new KeyEvent(Now(), key, IsDown: false));
                return Outcome.DoneWithInput;
        }
        // The remaining steps act on a window; they wait until a person could.
        if (words.Length < 2) return Fail(shell, step, "names no window");
        if (shell.Phase is ShellPhase.InMatch or ShellPhase.InMission || shell.Stack.Count == 0 || shell.CurrentScreen.IsClosing || shell.IsAnimating) return Outcome.Wait;
        var tree = shell.CurrentScreen.Tree;
        if (tree.FindByName(words[1]) is not { } window || !window.IsVisible || !window.IsEffectivelyEnabled) return Outcome.Wait;
        var argument = Expand(step.Rest[words[1].Length..].Trim());
        switch (step.Verb)
        {
            case "click":
                Click(shell, window.Rect.X + window.Rect.Width / 2, window.Rect.Y + window.Rect.Height / 2);
                return Outcome.DoneWithInput;
            case "enter":
                if (window.TextEntry is null && window.ComboBox is not { Data.IsEditable: true }) return Fail(shell, step, "is not a field");
                tree.SetFocus(window);
                Send(shell, new KeyEvent(Now(), KeyCode.Enter, IsDown: true));
                Send(shell, new KeyEvent(Now(), KeyCode.Enter, IsDown: false));
                return Outcome.DoneWithInput;
            case "type":
            {
                var edit = window.TextEntry ?? (window.ComboBox is { Data.IsEditable: true } editable ? editable.Edit : null);
                if (edit is null) return Fail(shell, step, "is not a field");
                var field = window.ComboBox is null ? window.Rect : UiGadgetGeometry.ComboEdit(window);
                Click(shell, field.X + field.Width / 2, field.Y + field.Height / 2);
                Send(shell, new KeyEvent(Now(), KeyCode.End, IsDown: true));
                for (var index = edit.Text.Length; index > 0; index--) Send(shell, new KeyEvent(Now(), KeyCode.Backspace, IsDown: true));
                foreach (var character in argument) Send(shell, new TextInputEvent(Now(), character));
                if (edit.Text != argument) return Fail(shell, step, $"the field took '{edit.Text}'");
                return Outcome.DoneWithInput;
            }
            case "select":
            {
                if (window.ComboBox is not { } combo) return Fail(shell, step, "is not a combo box");
                var index = IndexOf(combo.Entries, argument);
                if (index < 0) return Fail(shell, step, $"has no entry with '{argument}' (it has: {string.Join(" | ", combo.Entries)})");
                if (!combo.IsOpen)
                {
                    var button = UiGadgetGeometry.ComboButton(window);
                    Click(shell, button.X + button.Width / 2, button.Y + button.Height / 2);
                }
                if (!combo.IsOpen) return Fail(shell, step, "did not open");
                var height = UiGadgetGeometry.RowHeight(window);
                var drop = UiGadgetGeometry.ComboDropDown(window);
                // An entry the open box does not show is scrolled to with the wheel over the box, a notch at a time, as a person would.
                for (var notch = 0; notch < 64 && (index < combo.TopIndex || window.Rect.Bottom + (index - combo.TopIndex) * height + height / 2 >= drop.Bottom); notch++)
                {
                    var top = combo.TopIndex;
                    Send(shell, new MouseWheelEvent(Now(), index < combo.TopIndex ? 1 : -1, new Vector2(drop.X + drop.Width / 3, drop.Y + drop.Height / 2)));
                    if (combo.TopIndex == top) break;
                }
                var row = index - combo.TopIndex;
                var y = window.Rect.Bottom + row * height + height / 2;
                if (row < 0 || y >= drop.Bottom) return Fail(shell, step, $"entry {index} is not among the rows the open box shows, and the wheel does not bring it there");
                Click(shell, drop.X + drop.Width / 3, y);
                if (combo.SelectedIndex != index) return Fail(shell, step, $"shows entry {combo.SelectedIndex} afterwards");
                return Outcome.DoneWithInput;
            }
            case "pick":
            {
                if (window.ListBox is not { } list) return Fail(shell, step, "is not a list");
                var index = IndexOf([.. list.Rows.Select(row => string.Join(" ", row.Cells))], argument);
                if (index < 0) return Outcome.Wait;
                var content = UiGadgetGeometry.ListContent(window);
                var height = UiGadgetGeometry.RowHeight(window);
                for (var notch = 0; notch < 256 && (index < list.TopIndex || content.Y + (index - list.TopIndex) * height + height / 2 >= content.Bottom); notch++)
                {
                    var top = list.TopIndex;
                    Send(shell, new MouseWheelEvent(Now(), index < list.TopIndex ? 1 : -1, new Vector2(content.X + content.Width / 2, content.Y + content.Height / 2)));
                    if (list.TopIndex == top) break;
                }
                var y = content.Y + (index - list.TopIndex) * height + height / 2;
                if (index < list.TopIndex || y >= content.Bottom) return Fail(shell, step, $"row {index} is not among the rows the list shows, and the wheel does not bring it there");
                Click(shell, content.X + Math.Min(20, content.Width / 2), y);
                if (list.SelectedIndex != index) return Fail(shell, step, $"has row {list.SelectedIndex} selected afterwards");
                return Outcome.DoneWithInput;
            }
        }
        return Fail(shell, step, "unknown step");
    }

    private static int IndexOf(IReadOnlyList<string> entries, string part)
    {
        for (var index = 0; index < entries.Count; index++)
            if (entries[index].Equals(part, StringComparison.Ordinal)) return index;
        for (var index = 0; index < entries.Count; index++)
            if (entries[index].Contains(part, StringComparison.Ordinal)) return index;
        return -1;
    }

    private bool Condition(GameShell shell, Step step, out string? problem)
    {
        problem = null;
        var words = step.Words;
        var subject = words[1].ToLowerInvariant();
        switch (subject)
        {
            case "seconds":
                if (!double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) { problem = "expected 'wait seconds N'"; return false; }
                if (seconds >= _limit) { problem = $"waits longer than the limit of {_limit} s"; return false; }
                return _waited >= seconds;
            case "phase":
                if (!Enum.TryParse<ShellPhase>(words[2], true, out var phase)) { problem = $"unknown phase '{words[2]}'"; return false; }
                return shell.Phase == phase;
            case "screen":
                return shell.Phase is not (ShellPhase.InMatch or ShellPhase.InMission) && shell.Stack.Count > 0 && !shell.CurrentScreen.IsClosing && !shell.IsAnimating
                       && shell.CurrentScreen.GetType().Name.Equals(words[2], StringComparison.OrdinalIgnoreCase);
            case "match":
            {
                // wait match NAME equals|atleast VALUE: what the host says about the running match.
                if (words.Length < 5) { problem = "expected 'match NAME equals|atleast VALUE'"; return false; }
                if (MatchProbe?.Invoke(words[2]) is not { } value) return false;
                if (words[3].Equals("equals", StringComparison.OrdinalIgnoreCase)) return value.Equals(Expand(words[4]), StringComparison.OrdinalIgnoreCase);
                if (words[3].Equals("atleast", StringComparison.OrdinalIgnoreCase))
                    return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var have) && long.TryParse(Expand(words[4]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var want) && have >= want;
                problem = $"unknown comparison '{words[3]}'";
                return false;
            }
        }
        if (shell.Phase is ShellPhase.InMatch or ShellPhase.InMission || shell.Stack.Count == 0) return false;
        var window = shell.CurrentScreen.Tree.FindByName(words[2]);
        switch (subject)
        {
            case "enabled": return window is { IsVisible: true, IsEffectivelyEnabled: true };
            case "disabled": return window is { IsVisible: true, IsEffectivelyEnabled: false };
            case "shown": return window is { IsVisible: true };
            case "hidden": return window is { IsVisible: false };
        }
        if (words.Length < 4) { problem = "incomplete condition"; return false; }
        var relation = words[3].ToLowerInvariant();
        var index = step.Rest.IndexOf(words[3], step.Rest.IndexOf(words[2], StringComparison.Ordinal) + words[2].Length, StringComparison.Ordinal);
        var expected = Expand(step.Rest[(index + words[3].Length)..].Trim());
        if (window is null) return false;
        switch (subject)
        {
            case "text":
            {
                var shown = (window.TextEntry?.Text ?? (window.ComboBox is { Data.IsEditable: true } combo ? combo.Edit.Text : null) ?? TextOf(shell, window)).Replace('\n', ' ');
                return relation switch
                {
                    "contains" => Any(expected, shown.Contains),
                    "lacks" => !shown.Contains(expected, StringComparison.Ordinal),
                    "equals" => shown.Equals(expected, StringComparison.Ordinal),
                    _ => Unknown(relation, out problem),
                };
            }
            case "list":
            {
                if (window.ListBox is not { } list) { problem = "is not a list"; return false; }
                var any = list.Rows.Any(row => Any(expected, string.Join(" ", row.Cells).Contains));
                return relation switch { "contains" => any, "lacks" => !any, _ => Unknown(relation, out problem) };
            }
            case "combo":
            {
                if (window.ComboBox is not { } box) { problem = "is not a combo box"; return false; }
                var current = box.SelectedIndex >= 0 && box.SelectedIndex < box.Entries.Count ? box.Entries[box.SelectedIndex] : string.Empty;
                return relation switch { "shows" => Any(expected, current.Contains), _ => Unknown(relation, out problem) };
            }
        }
        problem = $"unknown condition '{words[1]}'";
        return false;
    }

    private static bool Any(string alternatives, Func<string, bool> holds) => alternatives.Split('|').Any(holds);

    private static bool Unknown(string relation, out string? problem)
    {
        problem = $"unknown relation '{relation}'";
        return false;
    }

    private static string TextOf(GameShell shell, UiWindow window)
    {
        if (window.Text is not { } text) return string.Empty;
        return text.IsLabel && shell.TextSource is { } source && source.TryResolve(text.Value, out var resolved) ? resolved : text.Value;
    }

    private Outcome Fail(GameShell shell, Step step, string why)
    {
        Failed = true;
        var where = shell.Phase is ShellPhase.InMatch or ShellPhase.InMission || shell.Stack.Count == 0 ? $"phase {shell.Phase}" : $"phase {shell.Phase}, screen {shell.CurrentScreen.GetType().Name}";
        var seen = string.Empty;
        var named = step.Verb is "wait" or "expect" ? 2 : 1;
        if (step.Words.Length > named && shell.Phase is not (ShellPhase.InMatch or ShellPhase.InMission) && shell.Stack.Count > 0 && shell.CurrentScreen.Tree.FindByName(step.Words[named]) is { } window)
        {
            seen = $"; the window is {(window.IsVisible ? "shown" : "hidden")}, {(window.IsEffectivelyEnabled ? "enabled" : "disabled")}, text '{(window.TextEntry?.Text ?? TextOf(shell, window)).Replace('\n', ' ')}'";
            if (window.ListBox is { } list) seen += $", rows [{string.Join(" | ", list.Rows.Select(row => string.Join(" ", row.Cells)))}]";
            if (window.ComboBox is { } combo) seen += $", entries [{string.Join(" | ", combo.Entries)}] selected {combo.SelectedIndex}";
        }
        // What the lists of the screen said last (a room's chat list carries the server's refusals).
        if (shell.Phase is not (ShellPhase.InMatch or ShellPhase.InMission) && shell.Stack.Count > 0)
            foreach (var listed in shell.CurrentScreen.Tree.AllWindows.Where(candidate => candidate.IsVisible && candidate.ListBox is { Rows.Count: > 0 }).Take(4))
                seen += $"; {listed.Name} ends with [{string.Join(" | ", listed.ListBox!.Rows.TakeLast(3).Select(row => string.Join(" ", row.Cells)))}]";
        // A message over the screen is usually the reason: say what it says.
        if (shell.Stack.Count > 0 && shell.CurrentScreen is Screens.MessageBoxScreen box) seen += $"; a message is open: '{box.Title}' - '{box.Message.Replace('\n', ' ')}'";
        Write($"shell-script: FAILED step {step.Line} ({step.Text}): {why} - {where}{seen}");
        return Outcome.Failed;
    }

    private void Write(string line)
    {
        _log.Add(line);
        Trace?.Invoke(line);
    }

    private long Now() => (long)_clock + ++_events;

    private void Send(GameShell shell, InputEvent input) => shell.HandleInput(input);

    private void Click(GameShell shell, int x, int y)
    {
        var at = new Vector2(x, y);
        Send(shell, new MouseMoveEvent(Now(), at));
        Send(shell, new MouseButtonEvent(Now(), MouseButton.Left, true, at));
        Send(shell, new MouseButtonEvent(Now(), MouseButton.Left, false, at));
    }

    /// <summary>The summary line a check reads.</summary>
    public string Summary => $"shell-script summary: steps={_steps.Count} done={_next} failed={(Failed ? 1 : 0)} finished={(IsFinished && !Failed ? 1 : 0)}";

    /// <summary>The lines for the console at the end of a run: every step done, the failure if there was one, and the summary.</summary>
    public IReadOnlyList<string> Report()
        => [.. _log, $"shell-script summary: steps={_steps.Count} done={_next} failed={(Failed ? 1 : 0)} finished={(IsFinished && !Failed ? 1 : 0)}"];
}

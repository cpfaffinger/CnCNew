using System.Collections.Immutable;
using CnCNew.Ui.Runtime;

namespace CnCNew.Ui.Transitions;

/// <summary>One window of a running group: its effect plus the frame delay (<c>TransitionWindow</c>).</summary>
internal sealed class TransitionWindow(TransitionWindowSpec spec, ITransitionHost host, ISet<string> missing)
{
    private TransitionEffect? _effect;

    public bool IsFinished => _effect?.IsFinished ?? true;

    public int TotalFrames => spec.FrameDelay + (_effect?.FrameLength ?? 0);

    public void Init()
    {
        ITransitionWindow? window = null;
        if (spec.WindowName is { } name)
        {
            window = host.Find(name);
            if (window is null) missing.Add(name);
        }
        _effect = TransitionEffects.Create(spec.Style, host);
        _effect.Init(window);
    }

    public void Update(int groupFrame)
    {
        if (_effect is null) return;
        if (groupFrame < spec.FrameDelay || groupFrame > spec.FrameDelay + _effect.FrameLength) return;
        _effect.Update(groupFrame - spec.FrameDelay);
    }

    public void Reverse() => _effect?.Reverse();

    public void Skip() => _effect?.Skip();

    public void Draw(List<UiDrawCommand> output) => _effect?.Draw(output);
}

/// <summary>A running <see cref="TransitionGroupSpec"/>: a frame counter that runs forward, or backwards after <see cref="Reverse"/> (<c>TransitionGroup</c>).</summary>
internal sealed class TransitionGroup
{
    private readonly ImmutableArray<TransitionWindow> _windows;
    private int _direction = 1;
    private int _frame;
    private bool _forced;

    public TransitionGroup(TransitionGroupSpec spec, ITransitionHost host, ISet<string> missing)
    {
        Spec = spec;
        _windows = [.. spec.Windows.Select(window => new TransitionWindow(window, host, missing))];
    }

    public TransitionGroupSpec Spec { get; }

    public string Name => Spec.Name;

    public bool IsFireOnce => Spec.FireOnce;

    public bool IsReversed => _direction < 0;

    public bool IsFinished => _forced || _windows.All(window => window.IsFinished);

    /// <summary>The frame the group is on (for tests and diagnostics).</summary>
    public int Frame => _frame;

    public void Init()
    {
        _frame = 0;
        _direction = 1;
        _forced = false;
        foreach (var window in _windows) window.Init();
    }

    public void Update()
    {
        _frame += _direction;
        // The original lets a reversed group run on below zero if a window never reports finished; no shipped group does, but a
        // group that cannot end would block every screen that waits for it, so it is ended instead (and the case is observable).
        if (_frame < 0)
        {
            Skip();
            Overrun = true;
            _forced = true;
            return;
        }
        foreach (var window in _windows) window.Update(_frame);
    }

    /// <summary>Set when a reversed group ran below frame 0 without finishing (never with the shipped data; tests assert it).</summary>
    public bool Overrun { get; private set; }

    public void Reverse()
    {
        _direction = -1;
        var total = _windows.Length == 0 ? 0 : _windows.Max(window => window.TotalFrames);
        foreach (var window in _windows) window.Reverse();
        _frame = total;
    }

    public void Skip()
    {
        foreach (var window in _windows) window.Skip();
    }

    public void Draw(List<UiDrawCommand> output)
    {
        foreach (var window in _windows) window.Draw(output);
    }
}

/// <summary>
/// The window transition handler of one screen (<c>GameWindowTransitionsHandler</c>): at most one <em>current</em> group runs, one more can be
/// <em>pending</em>; <see cref="SetGroup"/> reverses the running group (unless it is <c>FireOnce</c>) and starts the new one after it,
/// <see cref="Reverse"/> plays a group backwards (a group that is not running is first brought to its end state, which is how the
/// original plays a "Back" group), <see cref="Remove"/> drops a group, <see cref="IsFinished"/> tells screens when to move on.
/// <para>
/// Time: the original advances one frame per client frame (retail runs the shell at 30 FPS, <c>GameData.ini</c>
/// <c>FramesPerSecondLimit</c>); here <see cref="Advance"/> turns the caller's seconds into whole frames at
/// <see cref="FramesPerSecond"/> and <see cref="Step"/> advances exactly one. Nothing reads wall time, so a sequence is a pure function
/// of the frames stepped.
/// </para>
/// Unknown group names and windows that are not in the screen are never ignored: an unknown group throws, a missing window is recorded in
/// <see cref="MissingWindows"/> (and its effect does nothing).
/// </summary>
public sealed class WindowTransitionHandler
{
    /// <summary>Transition frames per second (<c>GameData.ini</c>: <c>UseFPSLimit = Yes</c>, <c>FramesPerSecondLimit = 30</c>).</summary>
    public const int FramesPerSecond = 30;

    private readonly WindowTransitionTable _table;
    private readonly ITransitionHost _host;
    private readonly Dictionary<string, TransitionGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _missing = new(StringComparer.Ordinal);
    private TransitionGroup? _current;
    private TransitionGroup? _pending;
    private TransitionGroup? _draw;
    private TransitionGroup? _secondaryDraw;
    private double _carry;

    public WindowTransitionHandler(WindowTransitionTable table, ITransitionHost host)
    {
        _table = table;
        _host = host;
    }

    /// <summary>Whole frames stepped so far.</summary>
    public long FramesStepped { get; private set; }

    /// <summary>Decorated names of windows a started group asked for that the screen does not have.</summary>
    public IReadOnlyCollection<string> MissingWindows => _missing;

    /// <summary>Mapped images an effect asked for that do not exist (the effect drew nothing for them).</summary>
    public IReadOnlyCollection<string> MissingImages => _host.MissingImages;

    public string? CurrentGroup => _current?.Name;

    public string? PendingGroup => _pending?.Name;

    public bool CurrentIsReversed => _current?.IsReversed ?? false;

    /// <summary>Whether any group ended by running past frame 0 (see <c>TransitionGroup.Overrun</c>).</summary>
    public bool AnyOverrun => _groups.Values.Any(group => group.Overrun);

    /// <summary>The current group has no unfinished window (<c>isFinished</c>); true when none runs. A pending group does not count, as in the original.</summary>
    public bool IsFinished => _current?.IsFinished ?? true;

    /// <summary>Nothing runs, nothing is pending: the screen is in its resting state.</summary>
    public bool IsIdle => _current is null && _pending is null;

    /// <summary>The names of the groups that were asked for so far (by <see cref="SetGroup"/>, <see cref="Reverse"/> or <see cref="Remove"/>), in the spelling of the table; for reports and tests.</summary>
    public IReadOnlyCollection<string> RequestedGroups => _groups.Values.Select(group => group.Name).Order(StringComparer.Ordinal).ToArray();

    private TransitionGroup Find(string name)
    {
        if (_groups.TryGetValue(name, out var existing)) return existing;
        var spec = _table.Find(name) ?? throw new KeyNotFoundException($"Unknown window transition group '{name}'.");
        var group = new TransitionGroup(spec, _host, _missing);
        _groups[name] = group;
        return group;
    }

    private TransitionGroup? FindOrNone(string name) => string.IsNullOrEmpty(name) ? null : Find(name);

    /// <summary>
    /// The original takes the group it draws from <see cref="Step"/>, so the frame between starting a group and its first step shows the windows the
    /// group just hid with none of its stand-ins (a one-frame flash of the bare screen, e.g. before a fade from black). The group that just became
    /// current is drawn at once instead.
    /// </summary>
    private void PrimeDraw()
    {
        _draw = _current;
    }

    /// <summary>Starts a group; a running one is reversed first and the new one waits as pending (<c>setGroup</c>). <paramref name="immediate"/> skips the running group to its end instead.</summary>
    public void SetGroup(string groupName, bool immediate = false)
    {
        if (string.IsNullOrEmpty(groupName) && immediate) _current = null;
        if (immediate && _current is not null)
        {
            _current.Skip();
            _current = FindOrNone(groupName);
            _current?.Init();
            PrimeDraw();
            return;
        }
        if (_current is not null)
        {
            if (!_current.IsFireOnce && !_current.IsReversed) _current.Reverse();
            _pending = FindOrNone(groupName);
            _pending?.Init();
            return;
        }
        _current = FindOrNone(groupName);
        _current?.Init();
        PrimeDraw();
    }

    /// <summary>Plays a group backwards (<c>reverse</c>); see the class remarks for a group that is not the running one.</summary>
    public void Reverse(string groupName)
    {
        var group = Find(groupName);
        if (_current == group)
        {
            _current.Reverse();
            return;
        }
        if (_pending == group)
        {
            _pending = null;
            return;
        }
        _current?.Skip();
        _pending?.Skip();
        _current = group;
        _current.Init();
        _current.Skip();
        _current.Reverse();
        _pending = null;
        PrimeDraw();
    }

    /// <summary>Drops a group from the running or pending slot, bringing it to its end state first (<c>remove</c>).</summary>
    public void Remove(string groupName, bool skipPending = false)
    {
        var group = Find(groupName);
        if (_pending == group)
        {
            if (skipPending) _pending!.Skip();
            _pending = null;
        }
        if (_current == group)
        {
            _current!.Skip();
            _current = _pending is not null ? _pending : null;
        }
    }

    /// <summary>One transition frame (<c>update</c>).</summary>
    public void Step()
    {
        FramesStepped++;
        _secondaryDraw = _draw != _current ? _draw : null;
        _draw = _current;
        if (_current is not null && !_current.IsFinished) _current.Update();
        if (_current is not null && _current.IsFinished && _current.IsFireOnce) _current = null;
        if (_current is not null && _pending is not null && _current.IsFinished)
        {
            _current = _pending;
            _pending = null;
        }
        if (_current is null && _pending is not null)
        {
            _current = _pending;
            _pending = null;
        }
        if (_current is not null && _current.IsFinished && _current.IsReversed) _current = null;
    }

    /// <summary>Converts elapsed seconds into whole frames and steps them; the remainder carries over. Returns the frames stepped.</summary>
    public int Advance(double seconds)
    {
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds), "Time must not run backwards.");
        _carry += seconds * FramesPerSecond;
        var frames = (int)Math.Floor(_carry + 1e-9);
        _carry -= frames;
        for (var i = 0; i < frames; i++) Step();
        return frames;
    }

    /// <summary>The stand-ins of the groups drawn this frame, to be appended after the screen's own draw list (<c>draw</c>).</summary>
    public void AppendDraw(List<UiDrawCommand> output)
    {
        _draw?.Draw(output);
        _secondaryDraw?.Draw(output);
    }

    /// <summary>No group is waiting to start and the running one (if any) is finished: the screen shows its resting state.</summary>
    public bool IsSettled => _pending is null && (_current?.IsFinished ?? true);

    /// <summary>Steps until <see cref="IsSettled"/>, at most <paramref name="limit"/> frames; returns the frames stepped and throws if the groups never settle.</summary>
    public int Settle(int limit = 600)
    {
        for (var i = 0; i < limit; i++)
        {
            if (IsSettled) return i;
            Step();
        }
        if (IsSettled) return limit;
        throw new InvalidOperationException($"Window transitions did not settle within {limit} frames (current '{CurrentGroup}', pending '{PendingGroup}').");
    }
}

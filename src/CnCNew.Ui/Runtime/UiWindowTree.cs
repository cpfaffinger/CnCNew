using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>
/// Services a tree needs from its host. <see cref="DefaultTooltipDelayMs"/> is <c>TooltipDelayTime</c> of <c>Mouse.ini</c>
/// (retail 800); windows with <c>TOOLTIPDELAY</c> &gt;= 0 override it (<c>Mouse::setCursorTooltip</c>).
/// </summary>
public sealed record UiTreeServices(IUiTextMeasure TextMeasure)
{
    public IUiImageLookup? Images { get; init; }
    public IUiTextSource? Text { get; init; }
    public WndHeaderTemplateTable HeaderTemplates { get; init; } = WndHeaderTemplateTable.Empty;
    public int DefaultTooltipDelayMs { get; init; } = 800;
    public int DoubleClickMs { get; init; } = 500;
}

/// <summary>Counters of cases the tree resolved explicitly while building (never silent fallbacks).</summary>
public sealed record UiTreeStatistics(
    int Windows,
    int UnknownWindowTypes,
    int WindowsWithoutFont,
    int DuplicateNames,
    ImmutableSortedDictionary<string, int> UnknownHeaderTemplates);

/// <summary>
/// A live window hierarchy built from one or more <see cref="WndLayout"/>s: state, z-order, lookup by decorated name,
/// hit-testing, input routing with gadget behaviour, typed window messages with a callback registry, a tooltip state
/// machine driven by a caller-supplied clock, and draw list generation. The tree never reads wall time.
/// </summary>
public sealed partial class UiWindowTree
{
    /// <summary>The fallback font of windows without <c>FONT</c> (<c>FontDesc</c> default of <c>GlobalLanguage.cpp</c>).</summary>
    public static readonly WndFont FallbackFont = new("Arial Unicode MS", 12, false);

    private readonly List<UiWindow> _roots = [];
    private readonly List<UiWindow> _all = [];
    private readonly Dictionary<string, UiWindow> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action<UiWindowTree, UiEvent>> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<UiWindowTree, UiWindow, string?>> _tooltipProviders = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _unregistered = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _unknownTemplates = new(StringComparer.Ordinal);
    private readonly List<(UiEvent Event, UiWindow Source)> _pending = [];
    private int _nextOrder;
    private int _windowsWithoutFont;
    private int _duplicateNames;

    public UiWindowTree(UiViewport viewport, UiScaleOptions options, UiTreeServices services)
    {
        Viewport = viewport;
        Options = options;
        Services = services;
    }

    public UiViewport Viewport { get; private set; }
    public UiScaleOptions Options { get; private set; }
    public UiTreeServices Services { get; }
    /// <summary>Top-level windows in insertion order (later layouts above earlier ones).</summary>
    public IReadOnlyList<UiWindow> Roots => _roots;
    /// <summary>All windows in creation order (file order, parents before children).</summary>
    public IReadOnlyList<UiWindow> AllWindows => _all;
    public UiWindow? Focus { get; private set; }

    /// <summary>
    /// The non-gadget window that receives keys while no gadget has the focus: what a layout's init function gets by calling <c>winSetFocus</c> on one of its
    /// USER windows (the window whose <c>INPUTCALLBACK</c> handles Escape: <c>SaveLoadMenu</c> of <c>SaveLoad.wnd</c> is such a child; the root of that
    /// file has no input callback). Unlike a gadget focus it survives the window being hidden for a while by a transition, and it is only used when visible;
    /// without it (or when it is hidden) keys start at the topmost visible root as before.
    /// </summary>
    public UiWindow? KeyboardHome { get; set; }
    public long Now { get; private set; }
    public IReadOnlyList<UiEvent> PendingEvents => _pending.Select(entry => entry.Event).ToArray();
    public IReadOnlyDictionary<string, int> UnregisteredCallbacks => _unregistered;

    public UiTreeStatistics Statistics => new(_all.Count, _all.Count(window => window.Type == WndWindowType.Unknown),
        _windowsWithoutFont, _duplicateNames, _unknownTemplates.ToImmutableSortedDictionary(StringComparer.Ordinal));

    /// <summary>Creates the windows of a layout on top of the existing ones and returns its top-level windows.</summary>
    public ImmutableArray<UiWindow> AddLayout(WndLayout layout)
    {
        var roots = ImmutableArray.CreateBuilder<UiWindow>();
        foreach (var definition in layout.Windows)
        {
            var root = Create(definition, null, layout.FileName);
            _roots.Add(root);
            roots.Add(root);
            Place(root, null);
        }
        foreach (var window in roots.SelectMany(Descendants))
            if (window.TabControl is not null) window.SelectTab(0);
        return roots.ToImmutable();
    }

    /// <summary>Removes all windows of a layout (by <see cref="WndLayout.FileName"/>).</summary>
    public void RemoveLayout(string layoutFile)
    {
        var removed = _all.Where(window => window.LayoutFile == layoutFile).ToHashSet();
        if (Focus is not null && removed.Contains(Focus)) Focus = null;
        if (_hover is not null && removed.Contains(_hover)) _hover = null;
        if (_grab is not null && removed.Contains(_grab)) _grab = null;
        if (_openCombo is not null && removed.Contains(_openCombo)) _openCombo = null;
        _roots.RemoveAll(removed.Contains);
        _all.RemoveAll(removed.Contains);
        foreach (var name in _byName.Where(pair => removed.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
            _byName.Remove(name);
        _pending.RemoveAll(entry => removed.Contains(entry.Source));
    }

    /// <summary>Looks a window up by its decorated name, e.g. <c>MainMenu.wnd:ButtonSinglePlayer</c> (case-sensitive; first created wins).</summary>
    public UiWindow? FindByName(string name) => _byName.GetValueOrDefault(name);

    public void SetViewport(UiViewport viewport, UiScaleOptions? options = null)
    {
        Viewport = viewport;
        Options = options ?? Options;
        Relayout();
    }

    internal void Relayout()
    {
        foreach (var root in _roots) Place(root, null);
    }

    public void Register(string callbackName, Action<UiWindowTree, UiEvent> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackName);
        _handlers[callbackName] = handler;
    }

    /// <summary>Registers a <c>TOOLTIPCALLBACK</c> that returns the tooltip text for a window (null: no tooltip).</summary>
    public void RegisterTooltip(string callbackName, Func<UiWindowTree, UiWindow, string?> provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackName);
        _tooltipProviders[callbackName] = provider;
    }

    /// <summary>Top-level windows from bottom to top: <c>BELOW</c> windows, normal windows, <c>ABOVE</c> windows (<c>winRepaint</c>).</summary>
    public IEnumerable<UiWindow> RootsBottomToTop()
        => _roots.Where(root => (root.Status & WndStatus.Below) != 0)
            .Concat(_roots.Where(root => (root.Status & (WndStatus.Above | WndStatus.Below)) == 0))
            .Concat(_roots.Where(root => (root.Status & WndStatus.Above) != 0 && (root.Status & WndStatus.Below) == 0));

    /// <summary>
    /// The deepest visible, enabled window under a point (<c>GameWindow::winPointInChild</c>): later siblings are above
    /// earlier ones, children are only found inside their parent, hidden and disabled windows are skipped.
    /// </summary>
    public UiWindow? HitTest(int x, int y)
    {
        foreach (var root in RootsBottomToTop().Reverse())
            if (HitWindow(root, x, y) is { } hit) return hit;
        return null;
    }

    /// <summary>
    /// The deepest visible window at a point when it, or a window above it, is disabled (<c>GameWindow::winPointInAnyChild</c> with <c>ignoreEnableCheck</c>); <see langword="null"/> when
    /// the point is over enabled windows only (the mouse region then decides) or over none. Only tooltips ask for it: a disabled window takes no input.
    /// </summary>
    private UiWindow? DisabledWindowAt(int x, int y)
    {
        if (_openCombo is not null && UiGadgetGeometry.ComboDropDown(_openCombo).Contains(x, y)) return null;
        foreach (var root in RootsBottomToTop().Reverse())
        {
            if (root.IsHidden || !root.HitRect.Contains(x, y)) continue;
            var window = root;
            var disabled = !window.IsEnabled;
            for (var descended = true; descended;)
            {
                descended = false;
                for (var index = window.Children.Count - 1; index >= 0; index--)
                {
                    var child = window.Children[index];
                    if (child.IsHidden || !child.HitRect.Contains(x, y)) continue;
                    window = child;
                    disabled |= !child.IsEnabled;
                    descended = true;
                    break;
                }
            }
            return disabled ? window : null;
        }
        return null;
    }

    private static UiWindow? HitWindow(UiWindow window, int x, int y)
    {
        if (window.IsHidden || !window.IsEnabled || !window.HitRect.Contains(x, y)) return null;
        for (var index = window.Children.Count - 1; index >= 0; index--)
            if (HitWindow(window.Children[index], x, y) is { } hit) return hit;
        return window;
    }

    public static IEnumerable<UiWindow> Descendants(UiWindow window)
    {
        yield return window;
        foreach (var child in window.Children)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    /// <summary>
    /// Delivers the pending events in order (including events raised by handlers) to the <c>SYSTEMCALLBACK</c> of the
    /// source's owner (its parent), following <c>PassMessagesToParentSystem</c>/<c>PassSelectedButtonsToParentSystem</c>;
    /// keys go to the nearest <c>INPUTCALLBACK</c>. Unregistered names are counted in <see cref="UnregisteredCallbacks"/>.
    /// </summary>
    public ImmutableArray<UiDelivery> Dispatch()
    {
        const int limit = 100_000;
        var deliveries = ImmutableArray.CreateBuilder<UiDelivery>();
        for (var index = 0; index < _pending.Count; index++)
        {
            if (index >= limit) throw new InvalidOperationException("UI event dispatch did not settle; handlers keep raising events.");
            var (uiEvent, source) = _pending[index];
            var delivery = uiEvent.Kind == UiEventKind.Key ? RouteKey(uiEvent, source) : RouteSystem(uiEvent, source);
            deliveries.Add(delivery);
            if (delivery.Outcome == UiDeliveryOutcome.Handled) _handlers[delivery.Callback!](this, delivery.Event);
        }
        _pending.Clear();
        return deliveries.ToImmutable();
    }

    private UiDelivery RouteSystem(UiEvent uiEvent, UiWindow source)
    {
        // Gadgets send to their owner, which the script loader sets to the parent; a top-level gadget owns itself.
        var target = source.Parent ?? source;
        while (true)
        {
            var callback = target.Definition.Callbacks.System;
            var addressed = uiEvent with { Target = target.Name };
            if (callback is null || callback == UiBuiltinCallbacks.DefaultSystem || UiBuiltinCallbacks.IsGadgetFunction(callback))
                return new UiDelivery(addressed, callback, UiDeliveryOutcome.NoCallback);
            if (callback == UiBuiltinCallbacks.PassMessagesToParent
                || (callback == UiBuiltinCallbacks.PassSelectedButtonsToParent && PassesSelectedButtons(uiEvent.Kind)))
            {
                if (target.Parent is null) return new UiDelivery(addressed, callback, UiDeliveryOutcome.NoCallback);
                target = target.Parent;
                continue;
            }
            if (callback == UiBuiltinCallbacks.PassSelectedButtonsToParent)
                return new UiDelivery(addressed, callback, UiDeliveryOutcome.Filtered);
            if (_handlers.ContainsKey(callback)) return new UiDelivery(addressed, callback, UiDeliveryOutcome.Handled);
            _unregistered[callback] = _unregistered.GetValueOrDefault(callback) + 1;
            return new UiDelivery(addressed, callback, UiDeliveryOutcome.Unregistered);
        }
    }

    /// <summary>The kinds <c>PassSelectedButtonsToParentSystem</c> forwards (<c>GameWindowManager.cpp</c>).</summary>
    private static bool PassesSelectedButtons(UiEventKind kind) => kind is UiEventKind.ButtonSelected or UiEventKind.ButtonSelectedRight
        or UiEventKind.ButtonMouseEntering or UiEventKind.ButtonMouseLeaving or UiEventKind.EntryEditDone;

    private UiDelivery RouteKey(UiEvent uiEvent, UiWindow source)
    {
        string? lastUnregistered = null;
        for (var window = source; window is not null; window = window.Parent)
        {
            var callback = window.Definition.Callbacks.Input;
            if (callback is null || callback == UiBuiltinCallbacks.DefaultInput || UiBuiltinCallbacks.IsGadgetFunction(callback)) continue;
            var addressed = uiEvent with { Target = window.Name };
            if (callback == UiBuiltinCallbacks.BlockInput) return new UiDelivery(addressed, callback, UiDeliveryOutcome.Blocked);
            if (_handlers.ContainsKey(callback)) return new UiDelivery(addressed, callback, UiDeliveryOutcome.Handled);
            // An unknown input function is no function in the original, so the key travels on to the parent.
            _unregistered[callback] = _unregistered.GetValueOrDefault(callback) + 1;
            lastUnregistered = callback;
        }
        return new UiDelivery(uiEvent, lastUnregistered, lastUnregistered is null ? UiDeliveryOutcome.NoReceiver : UiDeliveryOutcome.Unregistered);
    }

    /// <summary>Whether a key starting at <paramref name="source"/> reaches a registered or blocking input callback.</summary>
    private bool KeyWouldBeReceived(UiWindow source)
    {
        for (var window = source; window is not null; window = window.Parent)
        {
            var callback = window.Definition.Callbacks.Input;
            if (callback is null) continue;
            if (callback == UiBuiltinCallbacks.BlockInput || _handlers.ContainsKey(callback)) return true;
        }
        return false;
    }

    internal void Emit(UiEventKind kind, UiWindow source, int value = 0, string? text = null)
        => _pending.Add((new UiEvent(kind, source.Name, value, text), source));

    /// <summary>
    /// Queues a message as if <paramref name="source"/> had sent it (the shell's equivalent of <c>winSendSystemMsg</c>
    /// on behalf of a gadget); it is routed like gadget messages on the next <see cref="Dispatch"/>.
    /// </summary>
    public void Post(UiEventKind kind, UiWindow source, int value = 0, string? text = null)
    {
        if (source.Tree != this) throw new ArgumentException("The window belongs to another tree.", nameof(source));
        Emit(kind, source, value, text);
    }

    internal void OnWindowHidden(UiWindow window)
    {
        foreach (var descendant in Descendants(window))
        {
            if (Focus == descendant) Focus = null;
            if (_openCombo == descendant) _openCombo.ComboBox!.IsOpen = false;
            if (_openCombo == descendant) _openCombo = null;
            if (_grab == descendant) _grab = null;
            if (_hover == descendant) _hover = null;
            // The original delivers GWM_MOUSE_LEAVING to a hidden mouse region with the next mouse event; without it a
            // button hidden while hovered or pressed (a sub-menu switch) would come back hilited.
            descendant.IsHilited = false;
            if (descendant.Type == WndWindowType.PushButton && (descendant.Status & WndStatus.CheckLike) == 0) descendant.IsSelected = false;
            if (descendant.Slider is { } slider) slider.Dragging = false;
        }
    }

    internal void ClearRadioGroup(UiWindow selected)
    {
        var group = selected.Definition.RadioButton?.Group ?? 0;
        foreach (var window in _all)
            if (window != selected && window.Type == WndWindowType.RadioButton && window.LayoutFile == selected.LayoutFile
                && (window.Definition.RadioButton?.Group ?? 0) == group)
                window.IsSelected = false;
    }

    private UiWindow Create(WndWindow definition, UiWindow? parent, string layoutFile)
    {
        var window = new UiWindow(this, definition, parent, layoutFile, _nextOrder++);
        _all.Add(window);
        parent?.AddChild(window);
        if (definition.Name is { } name && !_byName.TryAdd(name, window)) _duplicateNames++;
        if (definition.HeaderTemplate is { } template && Services.HeaderTemplates.Find(template) is null)
            _unknownTemplates[template] = _unknownTemplates.GetValueOrDefault(template) + 1;
        if (definition.Font is null && Services.HeaderTemplates.Find(definition.HeaderTemplate) is null) _windowsWithoutFont++;
        foreach (var child in definition.Children) Create(child, window, layoutFile);
        return window;
    }

    private void Place(UiWindow window, UiTransform? inherited)
    {
        var rect = window.DesignRect;
        var (transform, forChildren) = UiScaler.ForWindow(rect, inherited, Viewport, Options.Mode, Options.KeepBandAspect);
        window.Transform = transform;
        window.Rect = window.ScreenRectOverride ?? transform.Map(rect);
        window.HitRect = Options.MinimumHitTarget > 0 && window.IsInteractive ? window.Rect.ExpandTo(Options.MinimumHitTarget) : window.Rect;
        window.Font = ResolveFont(window);
        foreach (var child in window.Children) Place(child, forChildren);
    }

    /// <summary>A known <c>HEADERTEMPLATE</c> replaces the window <c>FONT</c> (<c>parseWindow</c>, <c>getFontFromTemplate</c>).</summary>
    private UiFont ResolveFont(UiWindow window)
    {
        var rect = window.Definition.Rect;
        if (Services.HeaderTemplates.Find(window.Definition.HeaderTemplate) is { } template)
            return new UiFont(template.FontName,
                UiScaler.ScaleFontSize(template.Point, true, Viewport, Options, rect.CreationWidth, rect.CreationHeight), template.Bold);
        var font = window.Definition.Font ?? FallbackFont;
        return new UiFont(font.Name, UiScaler.ScaleFontSize(font.Size, false, Viewport, Options, rect.CreationWidth, rect.CreationHeight), font.Bold);
    }

    /// <summary>Advances the caller-supplied clock (milliseconds); only tooltips depend on it.</summary>
    public void AdvanceTo(long milliseconds)
    {
        if (milliseconds < Now) throw new ArgumentOutOfRangeException(nameof(milliseconds), "The UI clock must not run backwards.");
        Now = milliseconds;
    }

    /// <summary>
    /// The tooltip for the hovered window once it has been hovered for its delay: a <c>TOOLTIPCALLBACK</c> takes
    /// precedence over <c>TOOLTIPTEXT</c> like <c>winProcessMouseEvent</c>; <c>GameWinDefaultTooltip</c> shows nothing.
    /// </summary>
    public UiTooltip? Tooltip
    {
        get
        {
            // The tooltip window is found without the enabled check (winProcessMouseEvent: winPointInAnyChild( x, y, TRUE, TRUE )): a disabled button explains itself, too.
            var hover = _grab is null && _pointerInside ? DisabledWindowAt(_hoverX, _hoverY) ?? _hover : _hover;
            if (hover is null || _grab is not null || !hover.IsVisible) return null;
            var delay = hover.Definition.TooltipDelay >= 0 ? hover.Definition.TooltipDelay : Services.DefaultTooltipDelayMs;
            if (Now - _hoverSince < delay) return null;
            var age = Now - _hoverSince - delay;
            if (TooltipOverride is { } custom)
            {
                var text = custom(hover);
                if (text is not null) return new UiTooltip(hover.Name, text, null, _hoverX, _hoverY, age);
            }
            var callback = hover.Definition.Callbacks.Tooltip;
            if (callback is not null)
            {
                if (callback == UiBuiltinCallbacks.DefaultTooltip) return null;
                if (!_tooltipProviders.TryGetValue(callback, out var provider))
                {
                    if (_countedTooltipMiss != hover)
                    {
                        _unregistered[callback] = _unregistered.GetValueOrDefault(callback) + 1;
                        _countedTooltipMiss = hover;
                    }
                    return null;
                }
                return provider(this, hover) is { } text ? new UiTooltip(hover.Name, text, null, _hoverX, _hoverY, age) : null;
            }
            if (hover.Definition.TooltipText is not { } label) return null;
            return Services.Text is { } source && source.TryResolve(label, out var resolved)
                ? new UiTooltip(hover.Name, resolved, label, _hoverX, _hoverY, age)
                : new UiTooltip(hover.Name, label, label, _hoverX, _hoverY, age);
        }
    }

    private UiWindow? _countedTooltipMiss;

    /// <summary>
    /// Tooltip text a host computes for a window (the control bar builds its command button descriptions in code, not from
    /// <c>TOOLTIPTEXT</c>); consulted before the window's own callback and text. Null from the function: the window decides.
    /// </summary>
    public Func<UiWindow, string?>? TooltipOverride { get; set; }
}

using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Transitions;

/// <summary>
/// Connects the transition effects to a <see cref="UiWindowTree"/>: window names resolve in the tree, the display size is the tree's viewport,
/// sounds go to a callback (the shell turns them into <c>PlayUiSoundOutput</c>s).
/// </summary>
public sealed class UiTransitionHost(UiWindowTree tree, Action<string> playSound) : ITransitionHost
{
    private readonly Dictionary<UiWindow, TreeWindow> _wrappers = [];

    public string? ControlBarArrowImage { get; init; }

    public int DisplayWidth => tree.Viewport.Width;

    public int DisplayHeight => tree.Viewport.Height;

    public ITransitionWindow? Find(string decoratedName)
    {
        if (tree.FindByName(decoratedName) is not { } window) return null;
        if (!_wrappers.TryGetValue(window, out var wrapper)) _wrappers[window] = wrapper = new TreeWindow(tree, window);
        return wrapper;
    }

    public void PlaySound(string eventName) => playSound(eventName);

    public bool TryGetImageSize(string image, out int width, out int height)
    {
        width = height = 0;
        return tree.Services.Images is { } images && images.TryGetSize(image, out width, out height);
    }

    private sealed class TreeWindow(UiWindowTree tree, UiWindow window) : ITransitionWindow
    {
        public string Name => window.Name;

        public UiRect Rect => window.Rect;

        public bool IsHidden => window.IsHidden;

        public void SetHidden(bool hidden) => window.SetHidden(hidden);

        public string? EnabledImage => window.EnabledImage0;

        public string? DisabledImage => window.Definition.GetDrawData(WndDrawDataKind.Disabled)[0].Image;

        public void SetEnabledImage(string? image) => window.SetEnabledImage0(image);

        public string Text
        {
            get
            {
                if (window.Text is not { } value) return string.Empty;
                if (!value.IsLabel) return value.Value;
                return tree.Services.Text is { } source && source.TryResolve(value.Value, out var resolved) ? resolved : value.Value;
            }
        }

        public void SetText(string text) => window.SetText(text);

        public void AppendButtonImages(int alpha, List<UiDrawCommand> output) => output.AddRange(tree.BuildPushButtonImages(window, alpha));

        public void AppendText(string text, List<UiDrawCommand> output) => output.AddRange(tree.BuildStaticTextLines(window, text));
    }
}

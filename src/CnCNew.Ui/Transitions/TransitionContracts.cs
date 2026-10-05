using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.Ui.Transitions;

/// <summary>
/// What an effect may do to one window: the operations <c>GameWindowTransitionsStyles.cpp</c> performs on its <c>GameWindow*</c>
/// (hide/show, read the enabled image and text, replace an image, set the text) plus the two draw helpers that need the window's
/// own look (<c>PushButtonImageDrawThree</c>, <c>drawTypeText</c>).
/// </summary>
public interface ITransitionWindow
{
    /// <summary>The decorated name (<c>MainMenu.wnd:Logo</c>).</summary>
    string Name { get; }

    /// <summary>The window rectangle in screen pixels (<c>winGetScreenPosition</c> + <c>winGetSize</c>).</summary>
    UiRect Rect { get; }

    bool IsHidden { get; }

    void SetHidden(bool hidden);

    /// <summary><c>winGetEnabledImage(0)</c>: the mapped image of the first enabled draw entry, null for <c>NoImage</c>.</summary>
    string? EnabledImage { get; }

    /// <summary><c>winGetDisabledImage(0)</c>.</summary>
    string? DisabledImage { get; }

    /// <summary><c>winSetEnabledImage(0, image)</c>.</summary>
    void SetEnabledImage(string? image);

    /// <summary>The displayed text of a static text (<c>GadgetStaticTextGetText</c>; labels are already resolved).</summary>
    string Text { get; }

    /// <summary><c>GadgetStaticTextSetText</c>.</summary>
    void SetText(string text);

    /// <summary><c>PushButtonImageDrawThree(window, alpha)</c>: the left/middle/right images of a push button, tinted white with <paramref name="alpha"/>, without text.</summary>
    void AppendButtonImages(int alpha, List<UiDrawCommand> output);

    /// <summary><c>drawTypeText</c>: <paramref name="text"/> drawn like the window's own text (window font and colours, centered or left margin, wrapped at width - 10).</summary>
    void AppendText(string text, List<UiDrawCommand> output);
}

/// <summary>The world the effects live in: window lookup (<c>winGetWindowFromId</c>), the display size, the audio events they fire and image sizes.</summary>
public interface ITransitionHost
{
    /// <summary>The window with this decorated name, or null (the effect then does nothing and the handler counts the miss).</summary>
    ITransitionWindow? Find(string decoratedName);

    int DisplayWidth { get; }

    int DisplayHeight { get; }

    /// <summary><c>TheAudio->addAudioEvent(AudioEventRTS(name))</c>.</summary>
    void PlaySound(string eventName);

    /// <summary>The control bar arrow image (<c>TheControlBar->getArrowImage()</c>), null when there is none.</summary>
    string? ControlBarArrowImage { get; }

    bool TryGetImageSize(string image, out int width, out int height);

    /// <summary>Whether a mapped image of this name exists (<c>findImageByName</c> is not null).</summary>
    bool HasImage(string image);

    /// <summary>An effect wanted a mapped image that does not exist and drew nothing for it (the original's <c>drawImage(NULL)</c>); recorded so it can be reported, never silent.</summary>
    void NoteMissingImage(string image);

    /// <summary>The names recorded by <see cref="NoteMissingImage"/>, ordinal order.</summary>
    IReadOnlyCollection<string> MissingImages { get; }
}

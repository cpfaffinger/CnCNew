namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>ShellGameLoadScreen.wnd</c> (boot) and <c>SinglePlayerLoadScreen.wnd</c> (match start): both are pure display,
/// neither declares a <c>SYSTEMCALLBACK</c> or <c>INPUTCALLBACK</c> anywhere, so there is nothing to register. The
/// only moving part is <c>ProgressLoad</c> (a <c>PROGRESSBAR</c> both files share by name), driven by
/// <see cref="SetProgress"/>. <c>StaticTextLegal</c> (the EA copyright notice, present only on the boot screen) ships
/// <c>Hidden</c>; showing it here is the shell's equivalent of whatever init code the original used to reveal it.
/// </summary>
public sealed class LoadingScreen : ShellScreen
{
    public LoadingScreen(GameShell shell, string layoutName) : base(shell, layoutName)
    {
        TryW("StaticTextLegal")?.SetHidden(false);
    }

    public void SetProgress(int percent) => W("ProgressLoad").SetProgress(percent);
}

using CnCNew.Game.Hud;
using CnCNew.Game.Live;

namespace CnCNew.Game.Shell;

/// <summary>
/// The buttons of the in-game menu (<c>QuitMenu.wnd</c>, Escape) of a skirmish played on this machine, joined to the shell that shows what the original shows for them
/// (<c>QuitMenuSystem</c>): the options over the game, the save and load popup, the question before a restart and the question before leaving. The HUD only reports
/// the click (<see cref="MatchController.MenuActionRequested"/>); the shell's screens and outputs do the rest, so a host needs no code of its own for the buttons but
/// the handling of <see cref="EndMatchOutput"/>.
/// </summary>
public static class InGameMenu
{
    /// <summary>Joins the HUD of <paramref name="match"/> to <paramref name="shell"/>. A match of a server keeps the buttons the client has always given it (Options only; Exit leaves the room).</summary>
    public static void Attach(MatchController hud, LiveMatch match, GameShell shell)
    {
        ArgumentNullException.ThrowIfNull(hud);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(shell);
        hud.MenuActionRequested += action =>
        {
            if (shell.Phase != ShellPhase.InMatch || shell.HasInGameScreens) return;
            var local = match.Remote is null;
            switch (action)
            {
                case MenuAction.Options: shell.OpenInGameOptions(); break;
                case MenuAction.SaveLoad when local: shell.OpenInGameSaveLoad(new InGameSave(DefaultTitle(match), match.Save)); break;
                case MenuAction.Restart when local: shell.AskRestartMatch(match.Setup); break;
                case MenuAction.Exit when local: shell.AskLeaveMatch(); break;
            }
        };
    }

    /// <summary>The description the save popup offers: the map and how long the game has run, like the title of a mission save.</summary>
    public static string DefaultTitle(LiveMatch match)
    {
        var seconds = match.World.Time.ToWholeSeconds(match.World.Tick);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Skirmish, {Path.GetFileNameWithoutExtension(match.Setup.MapPath)} {seconds / 60}:{seconds % 60:D2}");
    }
}

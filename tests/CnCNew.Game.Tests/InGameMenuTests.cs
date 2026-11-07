using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Save;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;

namespace CnCNew.Game.Tests;

/// <summary>
/// The in-game menu (Escape, <c>QuitMenu.wnd</c>) of a skirmish, walked by a scripted person on the real data: a person starts a skirmish from the menus, opens the menu and
/// presses Options, Save/Load, Restart and Exit; each shows what the original shows (<c>QuitMenuSystem</c>). The host below does what the client does for the shell's outputs.
/// </summary>
internal static class InGameMenuTests
{
    private const int Width = 1280, Height = 720;

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        var contentDir = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var harness = new TestHarness(nameof(InGameMenuTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP InGameMenuTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The in-game menu: Options opens the options over the game and Back returns to the menu, the game stays paused", () => OptionsOverTheGame(data));
        harness.Run("The in-game menu: Save/Load saves the running skirmish under a description, asks before it overwrites, and loads it back into a running game", () => SaveAndLoad(data));
        harness.Run("The in-game menu: Restart asks, No keeps the game, Yes begins the same skirmish again behind the load screen", () => Restart(data));
        harness.Run("The in-game menu: Exit asks, No keeps the game, Yes ends the match and shows the score screen", () => Exit(data));
        return harness.Failures;
    }

    private static string Directory(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-ingame-menu-" + Guid.NewGuid().ToString("N"), name);
        System.IO.Directory.CreateDirectory(directory);
        return directory;
    }

    private const string ToTheMenu = """
        wait screen MainMenuScreen
        click MainMenu.wnd:ButtonSinglePlayer
        click MainMenu.wnd:ButtonSkirmish
        wait screen SkirmishSetupScreen
        click SkirmishGameOptionsMenu.wnd:ButtonStart
        limit 120
        wait phase InMatch
        wait match tick atleast 60
        limit 20
        key Escape
        wait match menu equals open
        expect match paused equals yes
        """;

    private static (Host Host, ShellScript Script, string Directory) Walk(GameData data, string name, string steps)
    {
        var directory = Directory(name);
        var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(Width, Height), skin: SkinPins.Current);
        shell.SkirmishMatchSeconds = 60;
        var script = ShellScript.Parse((ToTheMenu + "\n" + steps).Split('\n'));
        var host = new Host(data, shell, script);
        host.Attach();
        for (var frame = 0; frame < 40_000 && !script.IsFinished; frame++) host.Frame(0.05);
        foreach (var line in script.Report().Where(line => line.Contains("FAILED", StringComparison.Ordinal))) Console.WriteLine($"       {line}");
        TestHarness.Assert(script.IsFinished && !script.Failed, $"the script runs through ({script.StepsDone}/{script.StepCount})");
        return (host, script, directory);
    }

    private static void Done(string directory) => System.IO.Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);

    private static void OptionsOverTheGame(GameData data)
    {
        var (host, _, directory) = Walk(data, "options", """
            click QuitMenu.wnd:ButtonOptions
            wait screen OptionsScreen
            expect match overlay equals yes
            expect disabled OptionsMenu.wnd:ComboBoxResolution
            expect match paused equals yes
            click OptionsMenu.wnd:ButtonBack
            wait match overlay equals no
            expect match menu equals open
            expect match paused equals yes
            click QuitMenu.wnd:ButtonOptions
            wait screen OptionsScreen
            key Escape
            wait match overlay equals no
            expect match menu equals open
            click QuitMenu.wnd:ButtonReturn
            wait match menu equals closed
            expect match paused equals no
            """);
        TestHarness.Assert(host.Started == 1, "the same match went on through all of it");
        Done(directory);
    }

    private static void SaveAndLoad(GameData data)
    {
        var (host, _, directory) = Walk(data, "save", """
            click QuitMenu.wnd:ButtonSaveLoad
            wait screen LoadGameScreen
            expect match overlay equals yes
            expect list PopupSaveLoad.wnd:ListboxGames lacks Menu save
            click PopupSaveLoad.wnd:ButtonSave
            wait shown PopupSaveLoad.wnd:SaveDescParent
            type PopupSaveLoad.wnd:EntryDesc Menu save
            click PopupSaveLoad.wnd:ButtonSaveDescConfirm
            wait list PopupSaveLoad.wnd:ListboxGames contains Menu save
            click PopupSaveLoad.wnd:ButtonSave
            wait shown PopupSaveLoad.wnd:SaveDescParent
            type PopupSaveLoad.wnd:EntryDesc Menu save
            click PopupSaveLoad.wnd:ButtonSaveDescConfirm
            wait shown PopupSaveLoad.wnd:OverwriteConfirmParent
            click PopupSaveLoad.wnd:ButtonOverwriteCancel
            pick PopupSaveLoad.wnd:ListboxGames Menu save
            click PopupSaveLoad.wnd:ButtonLoad
            wait shown PopupSaveLoad.wnd:LoadConfirmParent
            click PopupSaveLoad.wnd:ButtonLoadConfirm
            limit 60
            wait match overlay equals no
            wait match menu equals closed
            """);
        TestHarness.Assert(host.SavedAtTick > 0, "the save was written from the running game");
        TestHarness.Assert(host.Started == 2 && host.Resumed == 1, $"the loaded game replaced the running one ({host.Started} games started, {host.Resumed} from a save)");
        TestHarness.Assert(host.ResumedHash == host.SavedHash && host.ResumedTick == host.SavedAtTick, $"it continues from the saved tick ({host.ResumedTick} against {host.SavedAtTick}) in the saved state ({host.ResumedHash:X16} against {host.SavedHash:X16})");
        var slots = new SaveGameStore(Path.Combine(directory, "Saves")).List();
        TestHarness.Assert(slots.Count(slot => slot.Header is { Title: "Menu save" }) == 1, $"one file titled 'Menu save' lies in the store, written once although it was asked for twice ({slots.Length} slots)");
        Done(directory);
    }

    private static void Restart(GameData data)
    {
        var (host, _, directory) = Walk(data, "restart", """
            click QuitMenu.wnd:ButtonRestart
            wait screen MessageBoxScreen
            expect match overlay equals yes
            expect match box equals GUI:RestartConfirmationTitle
            click MessageBox.wnd:ButtonNo
            wait match overlay equals no
            expect match menu equals open
            click QuitMenu.wnd:ButtonRestart
            wait screen MessageBoxScreen
            click MessageBox.wnd:ButtonYes
            limit 120
            wait phase InMatch
            wait match tick atleast 1
            wait match menu equals closed
            expect match paused equals no
            """);
        TestHarness.Assert(host.Started == 2 && host.Ended.SequenceEqual([MatchEnding.Restart]), $"one restart ended the first game and began a second ({host.Started} games, ended: {string.Join(", ", host.Ended)})");
        TestHarness.Assert(ReferenceEquals(host.FirstSetup, host.LastSetup) || host.FirstSetup == host.LastSetup, "the second game was made from the first one's setup");
        TestHarness.Assert(host.LastMatchTick < host.LastTickBeforeEnd, $"it began from the start (tick {host.LastMatchTick} against {host.LastTickBeforeEnd} before)");
        Done(directory);
    }

    private static void Exit(GameData data)
    {
        var (host, _, directory) = Walk(data, "exit", """
            click QuitMenu.wnd:ButtonExit
            wait screen MessageBoxScreen
            expect match box equals GUI:QuitPopupTitle
            click QuitMessageBox.wnd:ButtonNo
            wait match overlay equals no
            expect match menu equals open
            click QuitMenu.wnd:ButtonExit
            wait screen MessageBoxScreen
            click QuitMessageBox.wnd:ButtonYes
            limit 30
            wait phase Results
            wait screen ScoreScreenScreen
            """);
        TestHarness.Assert(host.Started == 1 && host.Ended.SequenceEqual([MatchEnding.Leave]), $"leaving ended the one game ({host.Ended.Count} ended)");
        TestHarness.Assert(host.FinishedAtTick == host.LastTickBeforeEnd, "the score screen was built for the tick the game stopped at");
        Done(directory);
    }

    /// <summary>What the client does with a running skirmish and the shell's outputs, headless.</summary>
    internal sealed class Host(GameData data, GameShell shell, ShellScript script)
    {
        private readonly List<InputEvent> _input = [];
        private LiveMatch? _match;
        private MatchController? _hud;
        public int Started { get; private set; }
        public int Resumed { get; private set; }
        public long SavedAtTick { get; private set; }
        public ulong SavedHash { get; private set; }
        public long ResumedTick { get; private set; }
        public ulong ResumedHash { get; private set; }
        public long LastMatchTick => _match?.Tick ?? -1;
        public long LastTickBeforeEnd { get; private set; }
        public long FinishedAtTick { get; private set; } = -1;
        public MatchSetup? FirstSetup { get; private set; }
        public MatchSetup? LastSetup { get; private set; }
        public List<MatchEnding> Ended { get; } = [];

        private static readonly Vector2 Viewport = new(Width, Height);

        public void Attach()
        {
            script.MatchInput = _input.Add;
            script.MatchPoint = window => _hud?.PressablePoint(window);
            script.MatchProbe = name => name switch
            {
                "tick" => _match?.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "menu" => _hud is null ? null : _hud.IsMenuOpen ? "open" : "closed",
                "paused" => _match is null ? null : _match.Paused ? "yes" : "no",
                "overlay" => _match is null ? null : shell.HasInGameScreens ? "yes" : "no",
                "box" => shell.Stack.Count > 0 && shell.CurrentScreen is MessageBoxScreen box ? box.Title : null,
                _ => null,
            };
        }

        public void Frame(double seconds)
        {
            script.Advance(shell, seconds);
            if (_match is { } match && _hud is { } hud)
            {
                // The events of the person go to the shell's screen when one lies over the game, to the HUD otherwise (ClientRuntime.Update).
                foreach (var input in _input)
                {
                    if (shell.HasInGameScreens) shell.HandleInput(input);
                    else hud.Process(input);
                }
                _input.Clear();
                if (shell.HasInGameScreens) shell.Update(seconds);
                match.Advance(seconds);
                // The game stands still while the menu is open: this is the state a save made from the menu holds.
                if (hud.IsMenuOpen && !shell.HasInGameScreens && SavedAtTick == 0 && Resumed == 0)
                {
                    SavedAtTick = match.Tick;
                    SavedHash = match.Session.StateHash();
                }
                hud.Update(seconds);
                _ = hud.BuildDrawList(Width, Height);
                if (shell.HasInGameScreens) _ = shell.BuildDrawList();
                Drain();
                return;
            }
            shell.Update(seconds);
            _ = shell.BuildDrawList();
            Drain();
        }

        private void Drain()
        {
            foreach (var output in shell.DrainOutputs())
            {
                switch (output)
                {
                    case StartMatchOutput start:
                        Begin(LiveMatch.Create(data, start.Setup, Viewport));
                        break;
                    case ResumeMatchOutput resume:
                        Begin(LiveMatch.Restore(data, resume.Save, Viewport));
                        Resumed++;
                        ResumedTick = _match!.Tick;
                        ResumedHash = _match.Session.StateHash();
                        break;
                    case EndMatchOutput ended when _match is { } ending:
                        Ended.Add(ended.EndedHow);
                        LastTickBeforeEnd = ending.Tick;
                        if (ended.EndedHow == MatchEnding.Leave)
                        {
                            FinishedAtTick = ending.Tick;
                            shell.MatchFinished(CnCNew.Game.Shell.MatchResult.From(ending.Players, ending.Winner, ending.World, ending.IsOver ? ending.EndTick : ending.Tick, null), null);
                        }
                        _match = null;
                        _hud = null;
                        break;
                }
            }
        }

        private void Begin(LiveMatch match)
        {
            Started++;
            _match = match;
            LastSetup = match.Setup;
            FirstSetup ??= match.Setup;
            _hud = new MatchController(match, SkinPins.Settings);
            InGameMenu.Attach(_hud, match, shell);
        }
    }
}

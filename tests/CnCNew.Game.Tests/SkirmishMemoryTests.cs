using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The skirmish setup keeps its slots (issue #379, the owner's playtest of 15 November 2026): <c>SkirmishPreferences::write</c> on Start and Back writes every slot
/// (<c>setSlotList</c>) - who sits in it, its army, team, colour and start place - and <c>SkirmishGameOptionsMenuInit</c> reads it back (<c>ParseAsciiStringToGameInfo</c>).
/// </summary>
internal static class SkirmishMemoryTests
{
    private const string EightPlayers = "Maps/Twilight Flame/Twilight Flame.map";
    private const string Setup = "SkirmishGameOptionsMenu.wnd:";

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        var harness = new TestHarness(nameof(SkirmishMemoryTests));
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP SkirmishMemoryTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The setup opens as it was left by Back, in the same game and in the next one", () => BackKeepsTheSlots(data));
        harness.Run("Start keeps the slots too, and the match it starts is the one the setup showed", () => StartKeepsTheSlots(data));
        harness.Run("Places are kept only on their map; a setup never left keeps its defaults", () => PlacesBelongToTheirMap(data));
        return harness.Failures;
    }

    private static GameShell Open(GameData data, string directory)
    {
        var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(1280, 720), skin: SkinPins.Current);
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSkirmish");
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen, "the setup opens");
        return shell;
    }

    private static void Post(GameShell shell, string window, int value)
    {
        var tree = shell.CurrentScreen.Tree;
        tree.Post(UiEventKind.ComboSelected, tree.FindByName(window) ?? throw new InvalidOperationException($"No window {window}."), value: value);
        tree.Dispatch();
    }

    private static int Selected(GameShell shell, string window) => shell.CurrentScreen.Tree.FindByName(window)!.ComboBox!.SelectedIndex;

    /// <summary>A setup changed in every kind of box: the person's army, team and colour; three armies of other strengths, profiles and teams; a closed slot; two places.</summary>
    private static SkirmishSetupScreen Change(GameShell shell)
    {
        var setup = (SkirmishSetupScreen)shell.CurrentScreen;
        setup.SetSelectedMap(EightPlayers);
        Post(shell, Setup + "ComboBoxPlayerTemplate0", 2);
        Post(shell, Setup + "ComboBoxTeam0", 1);
        Post(shell, Setup + "ComboBoxColor0", 3);
        Post(shell, Setup + "ComboBoxPlayer1", 5);          // rush army
        Post(shell, Setup + "ComboBoxPlayerTemplate1", 4);
        Post(shell, Setup + "ComboBoxTeam1", 2);
        Post(shell, Setup + "ComboBoxPlayer2", 4);          // hard army
        Post(shell, Setup + "ComboBoxTeam2", 1);
        Post(shell, Setup + "ComboBoxPlayer3", 1);          // closed
        Post(shell, Setup + "ComboBoxPlayer4", 2);          // easy army
        ShellTestInput.Click(shell, Setup + "ButtonMapStartPosition3");
        ShellTestInput.Click(shell, Setup + "ButtonMapStartPosition5");
        TestHarness.Assert(setup.AiOf(1) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Normal, AiTactics.Rush) && setup.AiOf(2) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Hard)
                           && setup.AiOf(4) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Easy), "the boxes set the AIs");
        return setup;
    }

    private static void SameAs(GameShell shell, System.Collections.Immutable.ImmutableArray<SkirmishSlotMemory> left, string when)
    {
        var setup = (SkirmishSetupScreen)shell.CurrentScreen;
        var now = setup.RememberedSlots();
        TestHarness.Assert(now.SequenceEqual(left), $"{when}: the slots are as they were left ({string.Join(" | ", now)} against {string.Join(" | ", left)})");
        TestHarness.Assert(setup.CurrentMapPath == EightPlayers, $"{when}: the map is kept");
        TestHarness.Assert(Selected(shell, Setup + "ComboBoxPlayerTemplate0") == 2 && Selected(shell, Setup + "ComboBoxTeam0") == 1 && Selected(shell, Setup + "ComboBoxPlayer1") == 5
                           && Selected(shell, Setup + "ComboBoxPlayerTemplate1") == 4 && Selected(shell, Setup + "ComboBoxTeam1") == 2 && Selected(shell, Setup + "ComboBoxPlayer2") == 4
                           && Selected(shell, Setup + "ComboBoxPlayer3") == 1 && Selected(shell, Setup + "ComboBoxPlayer4") == 2 && Selected(shell, Setup + "ComboBoxPlayer5") == 0,
            $"{when}: the boxes show the kept slots");
        TestHarness.Assert(setup.ColorOf(0) == left[0].Color && left[0].Color >= 0, $"{when}: the person's colour is kept");
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName(Setup + "ComboBoxPlayerTemplate1")!.IsEffectivelyEnabled
                           && !shell.CurrentScreen.Tree.FindByName(Setup + "ComboBoxPlayerTemplate3")!.IsEffectivelyEnabled, $"{when}: an army's faction box is enabled, a closed slot's not");
    }

    private static void BackKeepsTheSlots(GameData data)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-skirmish-memory-" + Guid.NewGuid().ToString("N"));
        try
        {
            var shell = Open(data, directory);
            var left = Change(shell).RememberedSlots();
            TestHarness.Assert(left[0].Start == 3 && left[1].Start == 5 && left[3].State == "closed" && left[5].State == "open" && left[1].Tactics == "rush" && left[2].Strength == "hard",
                $"the setup reads what it shows ({string.Join(" | ", left)})");
            ShellTestInput.Click(shell, Setup + "ButtonBack");
            TestHarness.Assert(shell.Settings.Skirmish.Slots.SequenceEqual(left), "Back writes the slots into the settings");
            ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
            ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSkirmish");
            SameAs(shell, left, "the same game");

            var next = Open(data, directory);
            SameAs(next, left, "the next game");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static void StartKeepsTheSlots(GameData data)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-skirmish-memory-" + Guid.NewGuid().ToString("N"));
        try
        {
            var shell = Open(data, directory);
            var left = Change(shell).RememberedSlots();
            ShellTestInput.Click(shell, Setup + "ButtonStart");
            shell.BuildDrawList();
            shell.Update(CnCNew.Tests.TestTime.FrameSeconds);
            var started = shell.DrainOutputs().OfType<StartMatchOutput>().Single().Setup;
            TestHarness.Assert(shell.Settings.Skirmish.Slots.SequenceEqual(left), "Start writes the slots into the settings");

            var next = Open(data, directory);
            SameAs(next, left, "after a start");
            ShellTestInput.Click(next, Setup + "ButtonStart");
            next.BuildDrawList();
            next.Update(CnCNew.Tests.TestTime.FrameSeconds);
            var again = next.DrainOutputs().OfType<StartMatchOutput>().Single().Setup;
            TestHarness.Assert(again.MapPath == started.MapPath && again.HumanFaction == started.HumanFaction && again.Opponents.SequenceEqual(started.Opponents) && again.Ai.SequenceEqual(started.Ai)
                               && again.Details.SequenceEqual(started.Details), "the kept setup starts the same match");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static void PlacesBelongToTheirMap(GameData data)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-skirmish-memory-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fresh = Open(data, directory);
            var defaults = ((SkirmishSetupScreen)fresh.CurrentScreen).RememberedSlots();
            TestHarness.Assert(defaults[1].State == "computer" && defaults[1].Strength == "medium" && defaults.Skip(2).All(slot => slot.State == "open") && defaults.All(slot => slot.Start == -1 && slot.Team == 0),
                "a setup never left shows its defaults: one medium army, the rest open, no places");

            var shell = Open(data, directory);
            var left = Change(shell).RememberedSlots();
            ShellTestInput.Click(shell, Setup + "ButtonBack");
            // The places were chosen on Twilight Flame; a file that names another map keeps the slots and forgets the places.
            var other = shell.Settings with { Skirmish = shell.Settings.Skirmish with { LastMap = "Maps/Alpine Assault/Alpine Assault.map" } };
            new SettingsStore(directory).Save(other);
            var next = Open(data, directory);
            var setup = (SkirmishSetupScreen)next.CurrentScreen;
            var now = setup.RememberedSlots();
            TestHarness.Assert(setup.CurrentMapPath == "Maps/Alpine Assault/Alpine Assault.map" && now.All(slot => slot.Start == -1), "no place survives onto another map");
            TestHarness.Assert(now[0].Faction == left[0].Faction && now[0].Team == left[0].Team && now[1].Tactics == "rush" && now[1].Team == 2,
                "the slots the map has keep their armies and teams");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}

using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;

namespace CnCNew.Game.Tests;

/// <summary>
/// The hotkeys of "More options" are rebindable (docs/UI-MODERN.md, docs/GAME-SHELL.md): a row is chosen and the next key pressed - with the modifiers held - becomes its binding;
/// a key another action already has is taken from it and the screen says so; "Defaults" brings the shipped keys back; Accept saves only what differs (settings schema 7), never the
/// installation's CommandMap.ini; and a match built from the saved settings, or told about them while it runs, reacts to the new key and no longer to the old one.
/// </summary>
internal static class HotkeyBindingTests
{
    private const int Width = 1280, Height = 720;

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(HotkeyBindingTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP HotkeyBindingTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
            harness.Run($"More options, {skin.Id} skin: a row is rebound by the next key with its modifiers, a taken key is taken over and told, Defaults restores, Accept saves the difference",
                () => Screen(data, skin));
        harness.Run("A match uses the player's bindings over the shipped ones: select all reacts to the new key and no longer to the old one, also when the settings change while it runs",
            () => Match(data));
        return harness.Failures;
    }

    private static void Press(GameShell shell, KeyCode key, params KeyCode[] held)
    {
        foreach (var modifier in held) shell.HandleInput(new KeyEvent(0, modifier, IsDown: true));
        shell.HandleInput(new KeyEvent(0, key, IsDown: true));
        shell.HandleInput(new KeyEvent(0, key, IsDown: false));
        foreach (var modifier in held.Reverse()) shell.HandleInput(new KeyEvent(0, modifier, IsDown: false));
    }

    private static void Screen(GameData data, UiSkin skin)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-hotkey-bindings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(Width, Height), skin: skin);
            ShellTestInput.Click(shell, "MainMenu.wnd:ButtonOptions");
            ShellTestInput.Click(shell, OptionsScreen.MoreOptionsButton);
            var screen = shell.CurrentScreen as MoreOptionsScreen ?? throw new InvalidOperationException("More options did not open");
            var shipped = screen.BindingOf(MetaAction.SelectAll) ?? throw new InvalidOperationException("select all has no shipped key");
            var stop = screen.BindingOf(MetaAction.Stop) ?? throw new InvalidOperationException("stop has no shipped key");
            TestHarness.Assert(!screen.CapturesKeys, "the screen takes no keys until a row is chosen");

            // A click on the row arms it, as the API does.
            var rows = screen.Tree.FindByName("MoreOptions.wnd:ListHotkeys")!.ListBox!.Rows;
            var selectAllRow = Enumerable.Range(0, rows.Count).First(row => screen.ActionAt(row) == MetaAction.SelectAll);
            screen.Tree.FindByName("MoreOptions.wnd:ListHotkeys")!.SetListSelection(selectAllRow);
            screen.SelectHotkey(MetaAction.SelectAll);
            TestHarness.Assert(screen.CapturesKeys && screen.HotkeyStatus.StartsWith("Press a key", StringComparison.Ordinal), $"a chosen row waits for a key ({screen.HotkeyStatus})");

            // Ctrl+Shift+F9: the modifiers held with the key belong to the binding, the modifier keys alone do not bind.
            Press(shell, KeyCode.F9, KeyCode.LeftControl, KeyCode.RightShift);
            TestHarness.Assert(screen.BindingOf(MetaAction.SelectAll) == new KeyBinding(KeyCode.F9, KeyModifiers.Control | KeyModifiers.Shift) && !screen.CapturesKeys,
                $"select all is Ctrl+Shift+F9 now ({screen.BindingOf(MetaAction.SelectAll)})");
            TestHarness.Assert(screen.HotkeyStatus.Contains("Ctrl+Shift+F9", StringComparison.Ordinal) && !screen.HotkeyStatus.Contains("taken", StringComparison.Ordinal), $"the screen names the key ({screen.HotkeyStatus})");
            TestHarness.Assert(rows[selectAllRow].Cells[1] == "Ctrl+Shift+F9", $"the list shows it ({rows[selectAllRow].Cells[1]})");

            // A conflict: stop's key is taken from stop, and the screen says so.
            screen.SelectHotkey(MetaAction.SelectAll);
            var keyHeld = (stop.Modifiers & KeyModifiers.Control) != 0 ? new[] { KeyCode.LeftControl } : [];
            Press(shell, stop.Key, keyHeld);
            TestHarness.Assert(screen.BindingOf(MetaAction.SelectAll) == stop && screen.BindingOf(MetaAction.Stop) is null && screen.HotkeyStatus.Contains("taken from", StringComparison.Ordinal),
                $"the key of stop moved to select all, stop is unbound, and the screen says so ({screen.HotkeyStatus})");
            TestHarness.Assert(rows.Any(row => row.Cells[1] == "Unbound"), "the unbound action stays listed as Unbound");

            // Escape gives up; it does not close the screen while a key is awaited.
            screen.SelectHotkey(MetaAction.Stop);
            Press(shell, KeyCode.Escape);
            TestHarness.Assert(shell.CurrentScreen == screen && !screen.CapturesKeys && screen.BindingOf(MetaAction.Stop) is null, "Escape cancels the capture and leaves the screen open");

            // Defaults restore the shipped keys.
            ShellTestInput.Click(shell, "MoreOptions.wnd:ButtonDefaults");
            TestHarness.Assert(screen.BindingOf(MetaAction.SelectAll) == shipped && screen.BindingOf(MetaAction.Stop) == stop, "Defaults bring the shipped keys back");

            // Accept saves only the difference.
            screen.SelectHotkey(MetaAction.SelectAll);
            Press(shell, KeyCode.F9, KeyCode.LeftControl);
            ShellTestInput.Click(shell, "MoreOptions.wnd:ButtonAccept");
            var saved = new SettingsStore(directory).Load();
            TestHarness.Assert(saved.Issues.IsEmpty && saved.Settings.Controls.Hotkeys.Count == 1 && saved.Settings.Controls.Hotkeys["SELECT_ALL"] == "CTRL KEY_F9 DOWN",
                $"the file holds only the changed binding ({string.Join(", ", saved.Settings.Controls.Hotkeys.Select(pair => pair.Key + "=" + pair.Value))}; {string.Join(", ", saved.Issues.Select(issue => issue.Path))})");

            // The screen opened again shows the saved key; Defaults of the options screen leave the keys alone.
            ShellTestInput.Click(shell, OptionsScreen.MoreOptionsButton);
            var again = shell.CurrentScreen as MoreOptionsScreen ?? throw new InvalidOperationException("More options did not open again");
            TestHarness.Assert(again.BindingOf(MetaAction.SelectAll) == new KeyBinding(KeyCode.F9, KeyModifiers.Control), "the saved key is shown when the screen opens again");
            TestHarness.Assert(shell.Stack.All(item => item.Tree.UnregisteredCallbacks.Count == 0), "every callback is registered");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Match(GameData data)
    {
        var scenario = DemoScenarios.TankDuel;
        var custom = GameSettings.Default with
        {
            Controls = GameSettings.Default.Controls with
            {
                Hotkeys = System.Collections.Immutable.ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("SELECT_ALL", "CTRL KEY_F9 DOWN")]),
            },
        };

        var live = LiveScenario.Create(data, scenario with { Orders = [] }, new Vector2(Width, Height));
        var controller = new MatchController(live, GameSettings.Default);
        long time = 10_000;
        void Advance(int frames)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }
        void Press(KeyCode key, params KeyCode[] held)
        {
            foreach (var modifier in held) controller.Process(new KeyEvent(time += 16, modifier, true));
            controller.Process(new KeyEvent(time += 16, key, true));
            Advance(1);
            controller.Process(new KeyEvent(time += 16, key, false));
            Advance(1);
            foreach (var modifier in held.Reverse()) controller.Process(new KeyEvent(time += 16, modifier, false));
            time += 1000;
        }
        bool SelectsAll(KeyCode key, params KeyCode[] held)
        {
            controller.Input.Selection.Clear();
            Advance(1);
            Press(key, held);
            return !controller.Input.Selection.IsEmpty;
        }

        Advance(2);
        var old = controller.Input.Hotkeys.TryGetEntry(MetaAction.SelectAll, out var entry) ? entry.Binding : throw new InvalidOperationException("select all is unbound");
        var oldHeld = (old.Modifiers & KeyModifiers.Control) != 0 ? new[] { KeyCode.LeftControl } : [];
        TestHarness.Assert(old.Modifiers is KeyModifiers.None or KeyModifiers.Control, $"the shipped key of select all is simple ({old})");
        TestHarness.Assert(SelectsAll(old.Key, oldHeld), "premise: the shipped key selects everything");

        // Told while the match runs ("live after saving").
        controller.ApplyHotkeys(custom);
        TestHarness.Assert(!SelectsAll(old.Key, oldHeld), "after the bindings changed, the old key no longer selects everything");
        TestHarness.Assert(SelectsAll(KeyCode.F9, KeyCode.LeftControl), "the new key does");
        TestHarness.Assert(controller.Input.Hotkeys.TryGetEntry(MetaAction.Stop, out _), "bindings that were not changed stay");

        // Back to the shipped keys with empty bindings.
        controller.ApplyHotkeys(GameSettings.Default);
        TestHarness.Assert(SelectsAll(old.Key, oldHeld) && !SelectsAll(KeyCode.F9, KeyCode.LeftControl), "empty bindings are the shipped keys again");

        // A match built from the saved settings.
        var second = LiveScenario.Create(data, scenario with { Orders = [] }, new Vector2(Width, Height));
        var built = new MatchController(second, custom);
        TestHarness.Assert(built.Input.Hotkeys.Resolve(new KeyBinding(KeyCode.F9, KeyModifiers.Control), HotkeyContexts.Game).Contains(MetaAction.SelectAll)
            && !built.Input.Hotkeys.Resolve(old, HotkeyContexts.Game).Contains(MetaAction.SelectAll), "a match built from the settings starts with the player's keys");
    }
}

using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The hot keys of the command bar with real data (<c>HotKeyTranslator</c>, <c>HotKeyManager::searchHotKey</c>: the character after the <c>&amp;</c> of a button's translated label,
/// executed when the key is released without a modifier): for America, China and GLA the builder's construction menu and the production buttons of command centre, barracks and
/// war factory, each key pressed through the same event path as the client (<see cref="MatchController.Process"/>). Every enabled button with a hot key has to answer its key - also
/// when CommandMap.ini binds the same key to a meta action on the key-down (A: attack move, S: stop, Q: select all, ...), which the original runs first and the hot key afterwards.
/// </summary>
internal static class HotkeyTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(HotkeyTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP HotkeyTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var data = GameData.Load(args[index + 1]);
        foreach (var faction in FactoryExits.Factions)
            harness.Run($"Hot keys of the {faction} builder, command centre, barracks and war factory: every enabled button answers the key of its label", () => Faction(data, faction));
        return harness.Failures;
    }

    private static void Faction(GameData data, string faction)
    {
        var scenario = DemoScenarios.Find(FactoryExits.NameOf(faction, "all")) ?? throw new InvalidOperationException("no factory scene");
        // The scene without its own production orders: the queues hold only what the keys queue.
        var live = LiveScenario.Create(data, scenario with { Orders = [] }, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default);
        var issued = new List<(SimCommand Command, bool Accepted)>();
        live.CommandIssued += (command, result) => issued.Add((command, result.Accepted));
        long time = 10_000;
        void Advance(int frames)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }
        void Press(KeyCode key)
        {
            controller.Process(new KeyEvent(time += 16, key, true));
            Advance(1);
            controller.Process(new KeyEvent(time += 16, key, false));
            Advance(1);
            time += 1000;
        }

        Advance(2);
        var bound = Enumerable.Range(0, 26).Select(letter => (Key: KeyCode.A + letter, Metas: controller.Input.Hotkeys.Resolve(new KeyBinding(KeyCode.A + letter, KeyModifiers.None, KeyTransition.Down), HotkeyContexts.Game)))
            .Where(entry => !entry.Metas.IsEmpty).Select(entry => $"{entry.Key}={string.Join("+", entry.Metas)}");
        Console.WriteLine($"       letters CommandMap.ini binds on the key-down without a modifier: {string.Join(", ", bound)}");
        var failures = new List<string>();
        var (tested, working, disabled, withoutKey) = (0, 0, 0, 0);
        foreach (var tag in new[] { "builder", "commandcenter", "barracks", "warfactory" })
        {
            var entity = live.World.FindEntity(live.Tags[tag]) ?? throw new InvalidOperationException($"'{tag}' is gone");
            controller.Input.Selection.Replace([entity.Id]);
            Advance(1);
            var bar = controller.CommandBar;
            Console.WriteLine($"       {entity.Template.Name} ({bar.CommandSet})");
            for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
            {
                if (bar[slot] is not { } button) continue;
                var label = $"{button.Button.Kind} {(button.Button.Object.Length > 0 ? button.Button.Object : button.Button.Upgrade.Length > 0 ? button.Button.Upgrade : button.Button.Name)}";
                if (button.Hotkey is not { } character)
                {
                    withoutKey++;
                    Console.WriteLine($"         slot {slot,2} {label,-58} no hot key in '{button.Button.TextLabel}'");
                    continue;
                }
                var key = character is >= 'a' and <= 'z' ? KeyCode.A + (character - 'a') : KeyCode.None;
                var metas = key == KeyCode.None ? [] : controller.Input.Hotkeys.Resolve(new KeyBinding(key, KeyModifiers.None, KeyTransition.Down), HotkeyContexts.Game);
                var metaNote = metas.IsEmpty ? string.Empty : $", CommandMap also binds it to {string.Join("+", metas)}";
                if (!button.IsEnabled)
                {
                    disabled++;
                    Console.WriteLine($"         slot {slot,2} {label,-58} key {char.ToUpperInvariant(character)}: disabled ({button.Reason}){metaNote}");
                    continue;
                }
                if (button.Button.Kind is not (GuiCommandKind.DozerConstruct or GuiCommandKind.UnitBuild)) continue;
                if (key == KeyCode.None) { failures.Add($"{entity.Template.Name}: {label} has the hot key '{character}', which is not a letter"); continue; }

                tested++;
                var before = issued.Count;
                Press(key);
                string outcome;
                var ok = false;
                if (button.Button.Kind == GuiCommandKind.DozerConstruct)
                {
                    ok = controller.Input.Commands is { Mode: CommandModeKind.Placement, PendingButton: { } pending } && pending.Object == button.Button.Object;
                    outcome = ok ? "placement starts" : $"no placement (command mode {controller.Input.Commands.Mode}, pending {controller.Input.Commands.PendingButton?.Object ?? "none"})";
                    controller.Input.Commands.Cancel();
                }
                else
                {
                    var queued = issued.Skip(before).Where(entry => entry.Command is QueueProductionCommand queue && queue.Template == button.Button.Object).ToList();
                    ok = queued.Count == 1 && queued[0].Accepted;
                    outcome = ok ? "queued" : queued.Count == 0 ? $"nothing queued ({issued.Count - before} other command(s))" : $"{queued.Count} queue command(s), accepted {queued.Count(entry => entry.Accepted)}";
                    if (ok) live.Issue(entity.Owner, (sequence, tick) => new CancelProductionCommand(entity.Owner, sequence, tick, entity.Id, 0));
                }
                // The key may also be a meta action of CommandMap.ini that changes the selection (Q, W, E, ...) or opens a mode: put the scene back for the next button.
                controller.Input.Commands.Cancel();
                controller.Input.Selection.Replace([entity.Id]);
                Advance(1);
                if (ok) working++;
                else failures.Add($"{entity.Template.Name}: key {char.ToUpperInvariant(character)} for {label} -> {outcome}{metaNote}");
                Console.WriteLine($"         slot {slot,2} {label,-58} key {char.ToUpperInvariant(character)}: {(ok ? "works" : "FAILS")} - {outcome}{metaNote}");
            }
        }

        Console.WriteLine($"       {faction}: {working} of {tested} construct/build hot keys work; {disabled} button(s) disabled, {withoutKey} without a hot key");
        TestHarness.Assert(tested >= 8, $"{faction}: the scene offers construct and build buttons with hot keys ({tested})");
        TestHarness.Assert(failures.Count == 0, $"{failures.Count} hot key(s) do not work: {string.Join("; ", failures)}");
    }
}

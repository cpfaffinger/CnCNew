using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The owner's playtest of 10 November 2026: the idle worker button must count only idle workers. On real data (a skirmish of the human GLA, the modern skin): workers at a construction or at
/// a repair are not idle (<c>DozerPrimaryIdleState</c> has left the list when it exited, <c>InGameUI::removeIdleWorker</c>), the button reaches only the idle one and cycles through the
/// idle ones, and with every worker busy it is disabled and a press does nothing. The same press as a key.
/// </summary>
internal static class IdleWorkerTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(IdleWorkerTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP IdleWorkerTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Ctrl+I selects the next idle worker like the button, cycles through the idle ones and does nothing when none is idle; it is a listed, rebindable hotkey that no other binding shares", () => IdleKey(data));
        harness.Run("Workers at a construction or a repair are not idle: the button reaches only the idle ones, and with every worker busy it is disabled", () => BusyWorkers(data));
        return harness.Failures;
    }

    private sealed class Rig
    {
        public readonly LiveMatch Match;
        public readonly MatchController Controller;
        public readonly SimulationWorld World;
        private ulong _sequence = 2_000_000;
        private long _time = 10_000;

        public Rig(GameData data, string faction)
        {
            var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
            Match = LiveMatch.Create(data, new MatchSetup(mapPath, faction, ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard), new Vector2(1280, 720));
            Controller = new MatchController(Match, GameSettings.Default) { StarBlinks = false };
            World = Match.World;
            Advance((int)World.Time.OriginalFrames(4));
        }

        public SelectionModel Selection => Controller.Input.Selection;

        public void Advance(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                Match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }

        public Entity Spawn(string name, int dx, int dy, Entity near)
        {
            var known = World.Entities.Select(entity => entity.Id).ToHashSet();
            TestHarness.Assert(World.Submit(new SpawnCommand(PlayerId.System, _sequence++, World.Tick, name, Match.Human, near.X + Fixed.FromInt(dx), near.Y + Fixed.FromInt(dy), Fixed.Zero, Fixed.Zero)).Accepted, $"{name} is spawned");
            Advance((int)World.Time.OriginalFrames(2));
            return World.Entities.First(entity => entity.Template.Name == name && !known.Contains(entity.Id));
        }

        public Entity Build(Entity worker, params string[] buildings)
        {
            // The first legal site, nearest first, of the first building the player may begin: the other sites of the test are already there.
            foreach (var scale in new[] { 1, 2, 3, 4 })
            foreach (var building in buildings)
            foreach (var (dx, dy) in new[] { (300, 0), (-300, 0), (0, 300), (0, -300), (220, 220), (-220, -220), (220, -220), (-220, 220), (400, 100), (-400, 100) })
            {
                var command = new ConstructCommand(Match.Human, 0, 0, worker.Id, building, worker.X + Fixed.FromInt(dx * scale), worker.Y + Fixed.FromInt(dy * scale), Fixed.Zero);
                if (!World.Check(command).Accepted) continue;
                TestHarness.Assert(Match.Commands.Issue((sequence, tick) => command with { Sequence = sequence, Tick = tick }).Accepted, "the construction is ordered");
                Advance(2);
                return World.Entities.First(entity => entity.Owner == Match.Human && entity.Template.Name == building && entity.HasStatus(ObjectStatusBits.UnderConstruction) && entity.BuilderId == worker.Id);
            }
            throw new InvalidOperationException("no legal site found near the worker");
        }

        public void PressButton()
        {
            var rect = Controller.WindowRect("ControlBar.wnd:ButtonIdleWorker")!.Value;
            var at = new Vector2(rect.CenterX, rect.CenterY);
            Controller.Process(new MouseMoveEvent(_time += 16, at));
            Controller.Process(new MouseButtonEvent(_time += 16, MouseButton.Left, true, at));
            Controller.Process(new MouseButtonEvent(_time += 50, MouseButton.Left, false, at));
            _time += 1000;
            Advance(1);
        }

        public void PressKey(KeyCode key, params KeyCode[] held)
        {
            foreach (var modifier in held) Controller.Process(new KeyEvent(_time += 16, modifier, true));
            Controller.Process(new KeyEvent(_time += 16, key, true));
            Advance(1);
            Controller.Process(new KeyEvent(_time += 16, key, false));
            Advance(1);
            foreach (var modifier in held.Reverse()) Controller.Process(new KeyEvent(_time += 16, modifier, false));
            _time += 1000;
        }

        public bool ButtonEnabled
        {
            get
            {
                Controller.BuildDrawList(1280, 720);
                return Controller.IsWindowEnabled("ControlBar.wnd:ButtonIdleWorker");
            }
        }
    }

    private static void BusyWorkers(GameData data)
    {
        var rig = new Rig(data, "FactionGLA");
        var (world, human) = (rig.World, rig.Match.Human);
        var center = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("COMMANDCENTER"));
        var first = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
        var second = rig.Spawn("GLAInfantryWorker", 80, 80, center);
        var third = rig.Spawn("GLAInfantryWorker", 100, 80, center);
        TestHarness.Assert(rig.Selection.IdleWorkerCount == 3 && rig.ButtonEnabled, $"three workers stand: three idle workers, the button is enabled ({rig.Selection.IdleWorkerCount})");

        // The first builds, the second repairs the hurt command center; the third stands.
        var site = rig.Build(first, "GLABarracks");
        TestHarness.Assert(world.Submit(new DamageCommand(PlayerId.System, 2_100_000, world.Tick, center.Id, center.Body!.MaxHealth / Fixed.FromInt(2), DamageTypes.Unresistable, 0)).Accepted, "the command center is hurt");
        rig.Advance(2);
        TestHarness.Assert(rig.Match.Commands.Issue((sequence, tick) => new RepairCommand(human, sequence, tick, second.Id, center.Id)).Accepted, "the repair is ordered");
        rig.Advance((int)world.Time.OriginalFrames(30));
        TestHarness.Assert(site.HasStatus(ObjectStatusBits.UnderConstruction) && center.Body!.Health < center.Body.MaxHealth, "the barracks is not finished and the center not yet repaired");
        TestHarness.Assert(rig.Selection.IdleWorkerCount == 1, $"one builds, one repairs, one stands: one idle worker ({rig.Selection.IdleWorkerCount})");

        rig.Selection.Replace([center.Id]);
        rig.PressButton();
        TestHarness.Assert(rig.Selection.Selected.SequenceEqual([third.Id]), $"the button selects the idle worker, not the one that builds or the one that repairs ({string.Join(",", rig.Selection.Selected)})");
        rig.PressButton();
        TestHarness.Assert(rig.Selection.Selected.SequenceEqual([third.Id]), "and again: the only idle worker");

        // The third goes to work as well: nobody is idle, the button is disabled and a press changes nothing.
        var other = rig.Build(third, "GLASupplyStash");
        rig.Advance((int)world.Time.OriginalFrames(10));
        TestHarness.Assert(other.HasStatus(ObjectStatusBits.UnderConstruction) && site.HasStatus(ObjectStatusBits.UnderConstruction) && center.Body!.Health < center.Body.MaxHealth, "all three are still at work");
        TestHarness.Assert(rig.Selection.IdleWorkerCount == 0 && !rig.ButtonEnabled, $"every worker is busy: no idle worker and the button is disabled ({rig.Selection.IdleWorkerCount})");
        rig.Selection.Replace([center.Id]);
        rig.PressButton();
        TestHarness.Assert(rig.Selection.Selected.SequenceEqual([center.Id]), $"a press with nobody idle selects nothing ({string.Join(",", rig.Selection.Selected)})");
    }

    private static void IdleKey(GameData data)
    {
        var rig = new Rig(data, "FactionGLA");
        var (world, human) = (rig.World, rig.Match.Human);
        var center = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("COMMANDCENTER"));
        var first = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
        var second = rig.Spawn("GLAInfantryWorker", 80, 80, center);
        var third = rig.Spawn("GLAInfantryWorker", 100, 80, center);

        // The binding: shipped as an engine default, alone on its key, and listed by the hotkey editor of More options.
        var hotkeys = rig.Controller.Input.Hotkeys;
        var ctrlI = new KeyBinding(KeyCode.I, KeyModifiers.Control, KeyTransition.Down);
        TestHarness.Assert(hotkeys.Resolve(ctrlI, HotkeyContexts.Game).SequenceEqual([MetaAction.SelectNextIdleWorker]), $"Ctrl+I is the idle worker key and nothing else ({string.Join(",", hotkeys.Resolve(ctrlI, HotkeyContexts.Game))})");
        TestHarness.Assert(hotkeys.Conflicts.IsEmpty, $"the shipped bindings have no conflict ({string.Join("; ", hotkeys.Conflicts.Select(conflict => conflict.Binding + " " + string.Join("+", conflict.Actions)))})");
        TestHarness.Assert(HotkeyMap.FromGraph(data.Graph).WithEngineDefaults().Entries.Any(entry => entry.Action == MetaAction.SelectNextIdleWorker && entry.Binding.Key == KeyCode.I), "the shipped map lists it, so the editor of More options does");

        // Two idle workers stand, the third builds: the key goes through the two idle ones, in the order they became idle, and round again.
        var site = rig.Build(third, "GLABarracks");
        rig.Advance((int)world.Time.OriginalFrames(2));
        TestHarness.Assert(rig.Selection.IdleWorkerCount == 2, $"two idle workers ({rig.Selection.IdleWorkerCount})");
        rig.Selection.Replace([center.Id]);
        rig.PressKey(KeyCode.I, KeyCode.LeftControl);
        var one = rig.Selection.Selected.ToArray();
        TestHarness.Assert(one.Length == 1 && one[0] != third.Id && (one[0] == first.Id || one[0] == second.Id), $"Ctrl+I selects an idle worker, not the one that builds ({string.Join(",", one)})");
        rig.PressKey(KeyCode.I, KeyCode.LeftControl);
        var two = rig.Selection.Selected.ToArray();
        TestHarness.Assert(two.Length == 1 && two[0] != one[0] && two[0] != third.Id, $"the next press selects the other idle worker ({string.Join(",", two)})");
        rig.PressKey(KeyCode.I, KeyCode.LeftControl);
        TestHarness.Assert(rig.Selection.Selected.SequenceEqual(one), "and the third press is the first again");
        rig.Advance(60);
        var looked = world.FindEntity(one[0])!;
        var target = rig.Match.Camera.Target;
        TestHarness.Assert(Math.Abs(target.X - (float)looked.X.ToDecimal()) < 40f && Math.Abs(target.Y - (float)looked.Y.ToDecimal()) < 40f, $"the camera looks at the worker ({target.X:F0},{target.Y:F0} for {looked.X.ToDecimal():F0},{looked.Y.ToDecimal():F0})");

        // I alone, or Ctrl with another key, is not the idle worker key.
        rig.Selection.Replace([center.Id]);
        rig.PressKey(KeyCode.I);
        TestHarness.Assert(rig.Selection.Selected.SequenceEqual([center.Id]), "I without Ctrl does not select a worker");

        // Every worker busy: the key does nothing.
        rig.Build(first, "GLASupplyStash", "GLABarracks");
        rig.Build(second, "GLASupplyStash", "GLABarracks");
        rig.Advance((int)world.Time.OriginalFrames(5));
        TestHarness.Assert(rig.Selection.IdleWorkerCount == 0 && site.HasStatus(ObjectStatusBits.UnderConstruction), $"every worker is busy ({rig.Selection.IdleWorkerCount})");
        rig.Selection.Replace([center.Id]);
        rig.PressKey(KeyCode.I, KeyCode.LeftControl);
        TestHarness.Assert(rig.Selection.Selected.SequenceEqual([center.Id]), $"Ctrl+I with nobody idle changes nothing ({string.Join(",", rig.Selection.Selected)})");
    }
}

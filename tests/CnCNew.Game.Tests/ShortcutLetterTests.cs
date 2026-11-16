using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Campaign;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// #389: <c>ControlBar::setControlCommand</c> gives every button it sets up the letter after its label's <c>&amp;</c> (<c>HotKeyManager::addHotKey</c>) - not only the slots of the
/// command bar but the buttons of the special powers bar (<c>populateSpecialPowerShortcut</c>) and of the general's window (<c>populatePurchaseScience</c>); a letter stays with the
/// first button that took it, the command bar's before the bar's before the window's. A key released without modifiers presses the button that holds it, and its tooltip names
/// that letter (#388). On real data: an American skirmish (Fuel Air Bomb &amp;B in the window, which the Mother of All Bombs, &amp;B as well, does not get) and the mission whose
/// powers bar carries the battleship's bombardment (&amp;F).
/// </summary>
internal static class ShortcutLetterTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ShortcutLetterTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ShortcutLetterTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The general's window: a science answers its label's letter while the window is open, after the command bar's letters, and its tooltip names it (#389)", () => Sciences(data));
        harness.Run("The powers bar: a button answers its label's letter when no slot of the command bar holds it, and its tooltip names it (#389)", () => PowersBar(data));
        harness.Run("The powers bar answers a letter with the bar of this frame, also when the HUD is not drawn (#401)", () => PowersBarWithoutDrawing(data));
        return harness.Failures;
    }

    private static long _time = 10_000;

    private static void Press(MatchController controller, Action advance, KeyCode key)
    {
        controller.Process(new KeyEvent(_time += 16, key, true));
        advance();
        controller.Process(new KeyEvent(_time += 16, key, false));
        advance();
        _time += 1000;
    }

    private static string? KeyLine(MatchController controller, string window)
        => (controller.Tree.FindByName(window) is { } found ? controller.Tree.TooltipOverride?.Invoke(found) : null)?.Split('\n').LastOrDefault(line => line.StartsWith(HotkeyText.Prefix, StringComparison.Ordinal));

    private static void Sciences(GameData data)
    {
        var setup = new MatchSetup(data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase)), "FactionAmerica", ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        void Advance()
        {
            match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        for (var i = 0; i < match.World.Time.OriginalFrames(4); i++) Advance();
        var world = match.World;
        var human = match.Human;
        TestHarness.Assert(world.SetRankLevel(human, 5) && world.SciencePointsOf(human) >= 1, $"the player reaches rank 5 with points to spend ({world.SciencePointsOf(human)})");
        Advance();
        TestHarness.Assert(world.Sciences.TryGet("SCIENCE_DaisyCutter", out var daisy), "the fuel air bomb is a science");
        bool Owned() => world.PlayerSciences(human).Has(daisy!.Index);
        var view = controller.Generals!;
        var fuelAir = view.Rank8.FirstOrDefault(button => button?.Button.Name == "Command_PurchaseScienceDaisyCutter");
        TestHarness.Assert(fuelAir is { Hotkey: 'b', IsEnabled: true }, $"the fuel air bomb is in the window, can be bought and holds B ({fuelAir?.Hotkey}, {fuelAir?.Availability}, {fuelAir?.Reason})");
        var moab = view.Rank8.FirstOrDefault(button => button?.Button.Name.Contains("MOAB", StringComparison.Ordinal) == true);
        TestHarness.Assert(moab is null || moab.Hotkey is null, "the Mother of All Bombs' &B came after the fuel air bomb's and holds nothing");

        var presses = new List<bool>();
        controller.Input.CommandHotkeyPressed += activated => presses.Add(activated);
        // The window is shut: its sciences answer nothing.
        Press(controller, Advance, KeyCode.B);
        TestHarness.Assert(!Owned() && presses.Count == 0, "with the general's window shut B buys nothing");

        // The command bar's letters were set up first: the dozer's B (its barracks) keeps the key while the window is open.
        controller.ToggleGenerals();
        Advance();
        var dozer = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
        controller.Input.Selection.Replace([dozer.Id]);
        Advance();
        var dozerB = controller.CommandBar.Slots.FirstOrDefault(button => button?.Hotkey == 'b');
        TestHarness.Assert(dozerB is not null, "the dozer's bar holds B");
        var window = "GeneralsExpPoints.wnd:ButtonRank8Number" + (fuelAir!.Slot - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        TestHarness.Assert(KeyLine(controller, window) is null, $"while the command bar holds B the fuel air bomb's tooltip names no key ({KeyLine(controller, window)})");
        Press(controller, Advance, KeyCode.B);
        TestHarness.Assert(!Owned() && presses is [true], $"B presses the dozer's {dozerB!.Button.Name}, not the science ({string.Join(",", presses)})");
        controller.Input.Commands.Cancel();
        controller.Input.Selection.Clear();
        Advance();

        // Nothing selected: B is the science's, and its tooltip says so.
        TestHarness.Assert(controller.IsGeneralsWindowOpen, "the window is still open");
        TestHarness.Assert(KeyLine(controller, window) == HotkeyText.Prefix + "B", $"the fuel air bomb's tooltip names B ({KeyLine(controller, window)})");
        Press(controller, Advance, KeyCode.B);
        TestHarness.Assert(Owned() && presses is [true, true], $"B buys the fuel air bomb ({string.Join(",", presses)})");
        // Bought, it is no longer enabled: the key finds it disabled (GUIClickDisabled) and does nothing more.
        Press(controller, Advance, KeyCode.B);
        TestHarness.Assert(presses is [true, true, false], $"pressed again, B finds the bought science disabled ({string.Join(",", presses)})");
    }

    private static void PowersBar(GameData data)
    {
        var run = MissionRun.Load(MissionContext.From(data), "Maps/MD_USA02/MD_USA02.map", MissionOptions.Standard);
        var live = LiveMission.Create(data, run, new Vector2(1280, 720));
        var mission = new MissionController(live, SkinPins.Settings);
        var controller = mission.Hud;
        // The client builds the HUD's picture every frame; the powers bar's state comes from the update (MatchController.UpdatePowerBar, #401), and what it shows is what answers.
        void Advance()
        {
            mission.Update(CnCNew.Tests.TestTime.FrameSeconds);
            mission.BuildDrawList(1280, 720);
        }
        var world = live.World;
        var human = live.Human;
        // The opening cinematic locks the input; the bar is judged after it.
        for (var frame = 0; frame < 6000 && (mission.Director.InputLocked || mission.Director.Letterbox || world.Tick < world.Time.TicksPerSecond * 2); frame++) Advance();
        TestHarness.Assert(!mission.Director.InputLocked, "the opening cinematic is over");
        // The mission's own powers bar (map.ini: SpecialPowerShortcutUSA) carries the battleships' bombardment, shown while the player has a battleship: one is put to sea for it.
        var anchor = world.Entities.Where(entity => entity.Owner == human && !entity.EffectivelyDead).OrderBy(entity => entity.Id.Value).First();
        ulong sequence = 4_000_000;
        TestHarness.Assert(world.Submit(new CnCNew.Simulation.Commands.SpawnCommand(CnCNew.Simulation.Ids.PlayerId.System, sequence++, world.Tick, "AmericaVehicleBattleShip", human,
            anchor.X, anchor.Y, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero)).Accepted, "a battleship is spawned");
        var limit = world.Tick + world.Time.TicksPerSecond * 40;
        while (world.Tick < limit && controller.Powers.FirstOrDefault(power => power.Hotkey == 'f') is not { IsEnabled: true }) Advance();
        Console.WriteLine($"       the bar at tick {world.Tick}: {string.Join(", ", controller.Powers.Select(power => $"{power.Button.Name}:{power.Hotkey}:{power.Availability}"))}");
        var battleship = controller.Powers.FirstOrDefault(power => power.Hotkey == 'f');
        TestHarness.Assert(battleship is { IsEnabled: true } && battleship.Button.TextLabel == "CONTROLBAR:BattleshipFire", $"the bar carries the battleships' bombardment, ready, with F ({battleship?.Button.Name}, {battleship?.Availability})");
        var window = PowerWindow(controller, battleship!);
        controller.Input.Selection.Clear();
        Advance();
        TestHarness.Assert(KeyLine(controller, window) == HotkeyText.Prefix + "F", $"its tooltip names F ({KeyLine(controller, window)})");

        var presses = new List<bool>();
        controller.Input.CommandHotkeyPressed += activated => presses.Add(activated);
        Press(controller, Advance, KeyCode.F);
        TestHarness.Assert(presses is [true] && controller.Input.Commands.Mode == CommandModeKind.ButtonTarget, $"F presses the bombardment: the pointer asks for its target ({string.Join(",", presses)}, {controller.Input.Commands.Mode})");
        controller.Input.Commands.Cancel();

        // A ranger's bar holds F (its flash-bang grenades) and was set up first: F is the ranger's - pressed, or found disabled -, the power waits, and its tooltip names no key.
        TestHarness.Assert(world.Submit(new CnCNew.Simulation.Commands.SpawnCommand(CnCNew.Simulation.Ids.PlayerId.System, sequence++, world.Tick, "AmericaInfantryRanger", human,
            anchor.X, anchor.Y, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero)).Accepted, "a ranger is spawned");
        for (var frame = 0; frame < 4; frame++) Advance();
        var ranger = world.Entities.Where(entity => entity.Owner == human && entity.Template.Name == "AmericaInfantryRanger" && !entity.EffectivelyDead).OrderByDescending(entity => entity.Id.Value).First();
        controller.Input.Selection.Replace([ranger.Id]);
        Advance();
        var rangerF = controller.CommandBar.Slots.FirstOrDefault(button => button?.Hotkey == 'f')
            ?? throw new InvalidOperationException($"the ranger's bar holds no F ({string.Join(", ", controller.CommandBar.Slots.Where(button => button?.Hotkey is not null).Select(button => button!.Button.Name + ":" + button.Hotkey))})");
        TestHarness.Assert(KeyLine(controller, window) is null, $"the bombardment's tooltip names no key now ({KeyLine(controller, window)})");
        Press(controller, Advance, KeyCode.F);
        TestHarness.Assert(presses.Count == 2 && presses[1] == rangerF.IsEnabled && controller.Input.Commands.Mode != CommandModeKind.ButtonTarget,
            $"F is the ranger's {rangerF.Button.Name} ({rangerF.Availability}), not the bombardment ({string.Join(",", presses)}, {controller.Input.Commands.Mode})");
        controller.Input.Commands.Cancel();
    }

    /// <summary>
    /// #401: <c>ControlBar::update</c> refreshes the powers bar every client frame whether or not it is drawn, so a letter acts on the bar of this frame. The mission whose bar carries
    /// the battleships' bombardment (&amp;F), with a battleship put to sea, played without ever drawing the HUD: the bombardment is on the bar, F aims it, and the first picture shows it.
    /// </summary>
    private static void PowersBarWithoutDrawing(GameData data)
    {
        var run = MissionRun.Load(MissionContext.From(data), "Maps/MD_USA02/MD_USA02.map", MissionOptions.Standard);
        var live = LiveMission.Create(data, run, new Vector2(1280, 720));
        var mission = new MissionController(live, SkinPins.Settings);
        var controller = mission.Hud;
        void Advance() => mission.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var world = live.World;
        var human = live.Human;
        for (var frame = 0; frame < 6000 && (mission.Director.InputLocked || mission.Director.Letterbox || world.Tick < world.Time.TicksPerSecond * 2); frame++) Advance();
        TestHarness.Assert(!mission.Director.InputLocked, "the opening cinematic is over");

        var anchor = world.Entities.Where(entity => entity.Owner == human && !entity.EffectivelyDead).OrderBy(entity => entity.Id.Value).First();
        TestHarness.Assert(world.Submit(new CnCNew.Simulation.Commands.SpawnCommand(CnCNew.Simulation.Ids.PlayerId.System, 4_100_000, world.Tick, "AmericaVehicleBattleShip", human,
            anchor.X, anchor.Y, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero)).Accepted, "a battleship is spawned");
        controller.Input.Selection.Clear();
        var limit = world.Tick + world.Time.TicksPerSecond * 40;
        while (world.Tick < limit && controller.Powers.FirstOrDefault(power => power.Hotkey == 'f') is not { IsEnabled: true }) Advance();
        var battleship = controller.Powers.FirstOrDefault(power => power.Hotkey == 'f');
        TestHarness.Assert(battleship is { IsEnabled: true } && battleship.Button.TextLabel == "CONTROLBAR:BattleshipFire",
            $"updated, not drawn: the bar carries the bombardment, ready, with F ({string.Join(", ", controller.Powers.Select(power => $"{power.Button.Name}:{power.Hotkey}:{power.Availability}"))})");
        TestHarness.Assert(controller.CommandBar.Slots.All(slot => slot?.Hotkey != 'f'), "no slot of the command bar holds F");

        var presses = new List<bool>();
        controller.Input.CommandHotkeyPressed += activated => presses.Add(activated);
        Press(controller, Advance, KeyCode.F);
        TestHarness.Assert(presses is [true] && controller.Input.Commands.Mode == CommandModeKind.ButtonTarget,
            $"F aims the bombardment without the HUD ever drawn ({string.Join(",", presses)}, {controller.Input.Commands.Mode})");
        controller.Input.Commands.Cancel();

        // Drawing reads the state the update made: the first picture shows the bombardment's button.
        mission.BuildDrawList(1280, 720);
        TestHarness.Assert(controller.Tree.FindByName(PowerWindow(controller, controller.Powers.First(power => power.Hotkey == 'f'))) is { IsHidden: false, IsEnabled: true }, "the first picture shows its button, enabled");
    }

    /// <summary>The window of the powers bar that shows <paramref name="power"/>.</summary>
    private static string PowerWindow(MatchController controller, CommandBarButton power)
    {
        var number = controller.Powers.IndexOf(power) + 1;
        var rect = controller.PowerButtonRect(number);
        var window = controller.Tree.AllWindows.First(candidate => candidate.Name.EndsWith(":ButtonCommand" + number.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            && candidate.Name.Contains("PowersShortcut", StringComparison.OrdinalIgnoreCase) && candidate.Rect == rect);
        return window.Name;
    }
}

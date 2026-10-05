using System.Numerics;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>A real campaign mission played through the skirmish HUD: <see cref="LiveMission"/> and <see cref="MissionController"/> (real content only).</summary>
internal static class MissionHudTests
{
    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        var harness = new TestHarness(nameof(MissionHudTests));
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP MissionHudTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1]);
        var viewport = new Vector2(1280, 720);

        harness.Run("A real mission runs behind the HUD: frames, control bar, orders through the session, selection reaches the scripts", () =>
        {
            var run = MissionRun.Load(MissionContext.From(data), "Maps/GC_ChemGeneral/GC_ChemGeneral.map", MissionOptions.Standard);
            var live = LiveMission.Create(data, run, viewport);
            var controller = new MissionController(live, GameSettings.Default);
            TestHarness.Assert(live.Frame.Drawables.Length > 0, "the mission's objects are drawn at the start");
            TestHarness.Assert(live.Players.Length >= 2 && !live.HumanFaction.Equals(string.Empty), $"the players and the viewer's faction are known (faction '{live.HumanFaction}')");
            for (var frame = 0; frame < 60; frame++) controller.Update(1.0 / 30);
            TestHarness.Assert(live.Tick >= 55, $"the mission advanced at 30 Hz (tick {live.Tick})");
            // The control bar is hidden while the opening cinematic's letterbox is up (CAMERA_LETTERBOX_BEGIN); the HUD is judged after it.
            for (var frame = 0; frame < 1500 && controller.Director.Letterbox; frame++) controller.Update(1.0 / 30);
            var list = controller.BuildDrawList(1280, 720);
            TestHarness.Assert(list.Commands.Any(command => command is CnCNew.Ui.Runtime.UiCustomDraw { Callback: "Viewport3D" }), $"the HUD draw list is built over the mission ({list.Commands.Length} commands)");

            var mine = live.World.Entities.Where(entity => entity.Owner == live.Human && !entity.EffectivelyDead && entity.Template.Locomotor is not null).OrderBy(entity => entity.Id.Value).First();
            var target = new Vector2((float)mine.X.ToDecimal() + 60, (float)mine.Y.ToDecimal());
            var accepted = live.Commands.Issue((sequence, tick) => new SetMoveTargetCommand(live.Human, sequence, tick, mine.Id, CnCNew.Simulation.Numerics.Fixed.FromDecimal((decimal)target.X), CnCNew.Simulation.Numerics.Fixed.FromDecimal((decimal)target.Y)));
            TestHarness.Assert(accepted.Accepted, "the human's order is accepted through the mission's command sink (the session's own sequence counter)");
            for (var frame = 0; frame < 10; frame++) controller.Update(1.0 / 30);
            TestHarness.Assert(mine.HasMoveTarget || mine.X.ToDecimal() != (decimal)(target.X - 60), "the unit obeys");

            if (run.Session.Engine.NamedObjects.Select(item => (item.Key, Entity: run.Session.World.FindEntity(new EntityId(item.Value)))).FirstOrDefault(item => item.Entity is not null && item.Entity.Owner == live.Human) is { Key: { Length: > 0 } name } named)
            {
                controller.Hud.Input.Selection.Replace([named.Entity!.Id]);
                TestHarness.Assert(run.Session.Engine.IsNamedSelected(name), $"selecting the named object '{name}' reaches the script engine (NAMED_SELECTED)");
            }
        });

        harness.Run("In a mission the human's dozer places a building through the control bar (clicks only)", () =>
        {
            var run = MissionRun.Load(MissionContext.From(data), "Maps/GC_ChemGeneral/GC_ChemGeneral.map", MissionOptions.Standard);
            var live = LiveMission.Create(data, run, viewport);
            var controller = new MissionController(live, GameSettings.Default);
            long time = 10_000;
            void Click(Vector2 at)
            {
                controller.Process(new MouseMoveEvent(time += 16, at));
                controller.Process(new MouseButtonEvent(time += 16, MouseButton.Left, true, at));
                // As in the client: the press and the release arrive in different frames, each of which updates the game and rebuilds the draw list.
                controller.Update(1.0 / 30);
                controller.BuildDrawList(1280, 720);
                controller.Process(new MouseMoveEvent(time += 16, at + new Vector2(1, 1)));
                controller.Update(1.0 / 30);
                controller.BuildDrawList(1280, 720);
                controller.Process(new MouseButtonEvent(time += 50, MouseButton.Left, false, at + new Vector2(1, 1)));
                time += 1000;
            }
            Vector2 Screen(Vector3 world)
            {
                TestHarness.Assert(live.Camera.TryWorldToScreen(world, out var screen), "the point projects onto the screen");
                return screen;
            }
            // The mission opens with a cinematic: its script disables the input and flies the camera over the enemy base (next test). The player builds afterwards.
            for (var frame = 0; frame < 1500 && (frame < 30 || controller.Director.InputLocked || controller.Director.CameraMoveActive); frame++) controller.Update(1.0 / 30);
            TestHarness.Assert(!controller.Director.InputLocked, $"the opening cinematic hands the input back (tick {live.Tick})");

            var dozer = live.World.Entities.First(entity => entity.Owner == live.Human && entity.Template.IsKindOf("DOZER"));
            var ground = SimulationQueries.GroundOf(dozer);
            live.Camera.CenterOn(ground);
            controller.Update(1.0 / 30);
            Click(Screen(EntityPicker.CenterOf(dozer)));
            controller.Update(1.0 / 30);
            TestHarness.Assert(controller.Hud.Input.Selection.Contains(dozer.Id), "a click selects the human's dozer");
            controller.BuildDrawList(1280, 720);

            var slot = Enumerable.Range(1, CommandBarModel.GridSlots).First(index => controller.Hud.CommandBar[index] is { Button.Kind: GuiCommandKind.DozerConstruct, IsEnabled: true });
            var template = controller.Hud.CommandBar[slot]!.Button.Object;
            var rect = controller.Hud.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}") ?? throw new InvalidOperationException("button window missing");
            Click(new Vector2(rect.CenterX, rect.CenterY));
            controller.Update(1.0 / 30);
            TestHarness.Assert(controller.Hud.Input.Commands.Mode == CommandModeKind.Placement, $"the construct button for '{template}' enters placement mode (mode {controller.Hud.Input.Commands.Mode})");

            PlacementGhost? ghost = null;
            var reasons = new List<string>();
            foreach (var offset in (ReadOnlySpan<Vector2>)[new(150, 0), new(-150, 0), new(0, 150), new(0, -150), new(110, 110), new(-110, -110), new(250, 0), new(0, 250)])
            {
                var candidate = ground + offset;
                if (!live.Camera.TryWorldToScreen(new Vector3(candidate.X, candidate.Y, 0f), out var at)) continue;
                controller.Process(new MouseMoveEvent(time += 16, at));
                if (controller.Hud.Input.Commands.Ghost is { IsValid: true } valid) { ghost = valid; break; }
                reasons.Add(controller.Hud.Input.Commands.Ghost?.Reason.ToString() ?? "no ghost");
            }
            TestHarness.Assert(ghost is not null, $"a buildable spot for '{template}' exists near the dozer ({string.Join(", ", reasons)})");
            Click(Screen(ghost!.Position));
            controller.Update(1.0 / 30);
            TestHarness.Assert(controller.Hud.Input.Commands.Mode == CommandModeKind.None, "the click places the structure");
            for (var frame = 0; frame < 60; frame++) controller.Update(1.0 / 30);
            var site = live.World.Entities.FirstOrDefault(entity => entity.Owner == live.Human && entity.Template.Name == template);
            TestHarness.Assert(site is not null, $"the construction site of '{template}' exists");
            var underConstruction = CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction;
            for (var frame = 0; frame < 2400 && site!.HasStatus(underConstruction); frame++) controller.Update(1.0 / 30);
            TestHarness.Assert(!site!.HasStatus(underConstruction) && !site.EffectivelyDead, $"the dozer builds it to the end (work {site.ConstructionWork}, dozer at {dozer.X.ToDecimal():F0},{dozer.Y.ToDecimal():F0}, site at {site.X.ToDecimal():F0},{site.Y.ToDecimal():F0}, tick {live.Tick})");
        });
        harness.Run("The opening cinematic locks the input, hides the control bar, flies the camera and gives everything back", () =>
        {
            var run = MissionRun.Load(MissionContext.From(data), "Maps/GC_ChemGeneral/GC_ChemGeneral.map", MissionOptions.Standard with { PresentationLatencyTicks = LiveMission.ClientPresentationLatencyTicks });
            var live = LiveMission.Create(data, run, viewport);
            live.Director.AudioControl = new FakeAudio(); // the volumes, mutes and Eva switch of the opening need a sound to act on
            var controller = new MissionController(live, GameSettings.Default);
            var start = live.Camera.Pivot;
            long time = 10_000;
            void Click(Vector2 at)
            {
                controller.Process(new MouseMoveEvent(time += 16, at));
                controller.Process(new MouseButtonEvent(time += 16, MouseButton.Left, true, at));
                controller.Update(1.0 / 30);
                controller.BuildDrawList(1280, 720);
                controller.Process(new MouseButtonEvent(time += 50, MouseButton.Left, false, at));
                time += 1000;
            }
            for (var frame = 0; frame < 90; frame++) controller.Update(1.0 / 30);
            var director = controller.Director;
            TestHarness.Assert(director.InputLocked && director.Letterbox, $"the intro disables the input and opens the letterbox (locked {director.InputLocked}, letterbox {director.Letterbox}, tick {live.Tick})");
            TestHarness.Assert(Vector2.Distance(live.Camera.Pivot, start) > 100f, $"the scripted camera left the player's base (from {start} to {live.Camera.Pivot})");
            var cinematic = controller.BuildDrawList(1280, 720);
            TestHarness.Assert(!cinematic.Commands.Any(command => command is CnCNew.Ui.Runtime.UiCustomDraw) && director.LetterboxFadeLevel == 1f,
                $"during the letterbox the control bar is hidden ({cinematic.Commands.Length} commands, fade level {director.LetterboxFadeLevel})");
            // W3DDisplay::renderLetterBox: the bars are what a 16:9 picture leaves free, so none at 1280x720 and 96 pixels at 1024x768.
            TestHarness.Assert(CnCNew.Game.Shell.MissionPresentationDirector.LetterboxBars(1280, 720, director.Letterbox, director.LetterboxFadeLevel).IsEmpty
                && director.BuildOverlayCommands(1024, 768).OfType<CnCNew.Ui.Runtime.UiFillRect>().Count(fill => fill.Rect.Width == 1024 && fill.Rect.Height == 96 && fill.Color.A == 255) == 2,
                "no bars on the 16:9 screen, two 96-pixel bars on a 4:3 one");

            // A click in the middle of the screen during the cinematic: before the fix it reached the world while the camera was being moved by the script.
            var dozer = live.World.Entities.First(entity => entity.Owner == live.Human && entity.Template.IsKindOf("DOZER"));
            var before = live.Camera.Pivot;
            Click(new Vector2(640, 344));
            TestHarness.Assert(controller.Hud.Input.Selection.Selected.Count() == 0 && controller.Hud.Input.Commands.Mode == CommandModeKind.None, "input is dropped while the script has it disabled");
            controller.Process(new MouseMoveEvent(time += 16, new Vector2(0, 0)));
            for (var frame = 0; frame < 20; frame++) controller.Update(1.0 / 30);
            TestHarness.Assert(director.CameraMoveActive || Vector2.Distance(live.Camera.Pivot, before) < 400f, "the pointer does not scroll the camera away from the scripted move");

            var frames = 0;
            for (; frames < 1500 && (director.InputLocked || director.CameraMoveActive); frames++) controller.Update(1.0 / 30);
            TestHarness.Assert(!director.InputLocked && !director.Letterbox, $"the script enables the input and closes the letterbox again (tick {live.Tick})");
            TestHarness.Assert(live.Tick is > 600 and < 1200, $"the cinematic takes its scripted time, about 25 seconds (ended at tick {live.Tick})");
            var hud = controller.BuildDrawList(1280, 720);
            TestHarness.Assert(hud.Commands.Any(command => command is CnCNew.Ui.Runtime.UiCustomDraw { Callback: "Viewport3D" }), "the control bar is back");
            TestHarness.Assert(Vector2.Distance(live.Camera.Pivot, SimulationQueries.GroundOf(dozer)) < 600f, $"the camera ends at the player's base (camera {live.Camera.Pivot}, dozer {SimulationQueries.GroundOf(dozer)})");

            live.Camera.CenterOn(SimulationQueries.GroundOf(dozer));
            controller.Update(1.0 / 30);
            TestHarness.Assert(live.Camera.TryWorldToScreen(EntityPicker.CenterOf(dozer), out var at), "the dozer is on screen");
            Click(at);
            controller.Update(1.0 / 30);
            TestHarness.Assert(controller.Hud.Input.Selection.Contains(dozer.Id), "after the cinematic a click selects the dozer");

            _ = director.PostEffects; // a renderer that draws the screen filters asks for them every frame
            var counts = director.Counts().ToDictionary(count => count.Action);
            foreach (var action in (ReadOnlySpan<string>)["DISABLE_INPUT", "ENABLE_INPUT", "CAMERA_LETTERBOX_BEGIN", "CAMERA_LETTERBOX_END", "SETUP_CAMERA", "MOVE_CAMERA_TO", "RESET_CAMERA"])
                TestHarness.Assert(counts.TryGetValue(action, out var count) && count.Emitted > 0 && count.Displayed == count.Emitted, $"{action} is emitted and displayed ({(counts.TryGetValue(action, out var value) ? $"{value.Emitted}/{value.Displayed}" : "never emitted")})");
            TestHarness.Assert(counts["SPEECH_PLAY"].Emitted > 0 && counts["SPEECH_PLAY"].Displayed == counts["SPEECH_PLAY"].Emitted, "speech without audio is shown as its placeholder subtitle");
            // Every output type of this map has a consumer since the second Phase 16 session (volumes, mutes, Eva, motion blur); what nothing shows is still counted, not dropped (MissionEffectsTests, MissionPresentationTests).
            TestHarness.Assert(counts.Values.All(count => count.Displayed + count.HandledElsewhere + count.NotDisplayed == count.Emitted && count.NotDisplayed == 0), "every output of the opening is accounted for as displayed: " + string.Join(", ", counts.Values.Where(count => count.NotDisplayed > 0).Select(count => count.Action)));
            TestHarness.Assert(counts["CAMERA_MOTION_BLUR_JUMP"].Forwarded == 1 && counts["MUSIC_SET_VOLUME"].Emitted > 0, "the motion blur and the music volume of the opening reach their consumers");
        });
        harness.Run("The mission ends through the HUD controller exactly once", () =>
        {
            var run = MissionRun.Load(MissionContext.From(data), "Maps/GC_ChemGeneral/GC_ChemGeneral.map", MissionOptions.Standard);
            var live = LiveMission.Create(data, run, viewport);
            var controller = new MissionController(live, GameSettings.Default);
            var ended = new List<MissionOutcome>();
            controller.MissionEnded += ended.Add;
            for (var frame = 0; frame < 30; frame++) controller.Update(1.0 / 30);
            foreach (var enemy in live.World.Entities.Where(entity => !entity.Owner.IsSystem && live.World.AreEnemies(live.Human, entity.Owner)).ToArray()) live.World.Kill(enemy.Id);
            for (var frame = 0; frame < 30 * 20 && ended.Count == 0; frame++) controller.Update(1.0 / 30);
            for (var frame = 0; frame < 30; frame++) controller.Update(1.0 / 30);
            TestHarness.Assert(ended.Count == 1 && ended[0] == MissionOutcome.Victory, $"the scripted victory ended the mission once (events: {string.Join(",", ended)}, outcome {live.Outcome})");
            TestHarness.Assert(live.Winner == live.Human, "the viewer is the winner of a won mission");
            TestHarness.Assert(controller.Director.OutcomeBanner == "MISSION ACCOMPLISHED" && controller.BuildDrawList(1280, 720).Commands.OfType<CnCNew.Ui.Runtime.UiTextDraw>().Any(text => text.Text == "MISSION ACCOMPLISHED"),
                $"the scripted victory is shown as a banner (banner '{controller.Director.OutcomeBanner}')");
            var victory = controller.Director.Counts().Single(count => count.Action == "VICTORY");
            TestHarness.Assert(victory is { Emitted: 1, Displayed: 1 }, $"the VICTORY output is counted as displayed ({victory.Emitted}/{victory.Displayed})");
        });
        return harness.Failures;
    }
}

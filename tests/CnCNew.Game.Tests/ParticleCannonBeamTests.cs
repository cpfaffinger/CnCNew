using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The owner's playtest of 10 November 2026 (issue 299), the USA superweapon on real data: the particle cannon fired and its beam was neither seen nor steerable by the pointer.
/// In the original the shortcut bar's launch ends with <c>CommandTranslator::issueSpecialPowerCommand</c> selecting the source ("because we just launched a special power via shortcut, and the
/// special power accepts input from the player (particle uplink cannon, spectre gunship), simply select the object now"), after which a click on the map drives the beam
/// (<c>ParticleUplinkCannonUpdate::setSpecialPowerOverridableDestination</c>: <c>ManualDrivingSpeed</c> a second, <c>ManualFastDrivingSpeed</c> after a second click within
/// <c>DoubleClickToFastDriveDelay</c>); the beam itself is the laser drawables the module creates (<c>createGroundToOrbitLaser</c>, <c>createOrbitToTargetLaser</c>, <c>createConnectorLasers</c>)
/// and the flares of its outer nodes and base (<c>setClientStatus</c>).
/// </summary>
internal static class ParticleCannonBeamTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ParticleCannonBeamTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ParticleCannonBeamTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The cannon launched from the shortcut bar is selected, the pointer is the uplink cannon's and a click drives the beam at the manual speed, a second click fast", () => Steering(data));
        return harness.Failures;
    }

    private sealed class Scene
    {
        public required LiveScenario Live { get; init; }
        public required MatchController Controller { get; init; }
        public required ParticleUplinkCannonUpdate Module { get; init; }
        public required CnCNew.Simulation.World.Entity Cannon { get; init; }
        public void Frame()
        {
            Live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }

        public void Seconds(double seconds)
        {
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * seconds; frame++) Frame();
        }
    }

    /// <summary>A cannon with power and its power ready; the player presses the button of the shortcut bar and clicks on the ground. Returns once the order is given.</summary>
    private static Scene FireFromShortcut(GameData data, out Vector3 aimed)
    {
        var scenario = new DemoScenario("cannon-beam", "cannon-beam", "cannon-beam", 6000,
            [new ScenarioUnit("cannon", "AmericaParticleCannonUplink", 1, -60, -60, 0f), new ScenarioUnit("plant1", "AmericaPowerPlant", 1, -140, 0, 0f), new ScenarioUnit("plant2", "AmericaPowerPlant", 1, -140, 70, 0f)], [], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        var cannon = live.World.Entities.First(entity => entity.Template.Name == "AmericaParticleCannonUplink");
        var scene = new Scene { Live = live, Controller = controller, Module = cannon.Modules.OfType<ParticleUplinkCannonUpdate>().Single(), Cannon = cannon };
        scene.Seconds(0.2);
        // The scenario's "powers ready" is overwritten when the building's construction ends; the owner waits four minutes for it.
        foreach (var power in cannon.Modules.OfType<CnCNew.Simulation.Modules.ISpecialPowerModule>()) power.SetReadyFrame(live.World.Tick);
        scene.Frame();
        controller.BuildDrawList(1280, 720);
        TestHarness.Assert(scene.Module.StatusAt(live.World) == ParticleUplinkCannonUpdate.UplinkStatus.ReadyToFire, $"the cannon is ready ({scene.Module.StatusAt(live.World)})");
        var button = controller.Powers.FirstOrDefault(value => value.Button.Name.Contains("ParticleUplinkCannon", StringComparison.OrdinalIgnoreCase));
        TestHarness.Assert(button is { IsEnabled: true }, $"the shortcut bar offers the cannon ({string.Join(", ", controller.Powers.Select(value => value.Button.Name))})");
        TestHarness.Assert(controller.Input.Selection.Selected.IsEmpty, "nothing is selected: the player pressed the bar, not the building");
        TestHarness.Assert(controller.PressPowerButton(controller.Powers.IndexOf(button!) + 1), "the press on the bar is taken");
        var first = new Vector2(640, 330);
        aimed = live.Camera.TryScreenToGround(first, out var point) ? point : throw new InvalidOperationException("the aim is not on the ground");
        controller.Process(new MouseMoveEvent(10_000, first));
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        controller.Process(new MouseButtonEvent(10_100, MouseButton.Left, true, first));
        controller.Process(new MouseButtonEvent(10_150, MouseButton.Left, false, first));
        return scene;
    }

    private static float Distance(Point3 from, float x, float y) => MathF.Sqrt(MathF.Pow((float)from.X.ToDecimal() - x, 2) + MathF.Pow((float)from.Y.ToDecimal() - y, 2));

    private static void Steering(GameData data)
    {
        var scene = FireFromShortcut(data, out var aimed);
        var (live, controller, module) = (scene.Live, scene.Controller, scene.Module);
        scene.Seconds(0.5);
        TestHarness.Assert(module.InUse && module.IsSteerable, "the cannon fires");
        TestHarness.Assert(controller.Input.Selection.Selected.Length == 1 && controller.Input.Selection.Selected[0] == scene.Cannon.Id, $"the launch from the shortcut bar selects the cannon ({controller.Input.Selection.Selected.Length} selected)");

        // The pointer over the map is the uplink cannon's.
        var second = new Vector2(420, 130);
        var secondGround = live.Camera.TryScreenToGround(second, out var target) ? target : throw new InvalidOperationException("the second place is not on the ground");
        controller.Process(new MouseMoveEvent(20_000, second));
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(controller.Cursor.Kind == CursorKind.ParticleUplinkCannon, $"the pointer is the uplink cannon's ({controller.Cursor.Kind})");

        // A click drives the beam towards it at the manual speed of 20 units a second.
        var distance = Distance(module.BeamPosition, secondGround.X, secondGround.Y);
        TestHarness.Assert(distance > 150f, $"setup: the second place is far from the beam ({distance}; first {aimed})");
        controller.Process(new MouseButtonEvent(20_100, MouseButton.Right, true, second));
        controller.Process(new MouseButtonEvent(20_150, MouseButton.Right, false, second));
        // The beam is born BeamTravelTime (2.5 s) after the order and drives from then on; what is clicked before is where it starts to drive to.
        scene.Seconds(2.5);
        var before = module.BeamPosition;
        scene.Seconds(2);
        var slow = Distance(module.BeamPosition, (float)before.X.ToDecimal(), (float)before.Y.ToDecimal());
        TestHarness.Assert(Math.Abs(slow - 40f) < 2f, $"the beam covers 20 units a second towards the click ({slow} in two seconds)");
        TestHarness.Assert(Distance(module.BeamPosition, secondGround.X, secondGround.Y) < distance - 38f, "and it came closer to it");

        // A second click inside the double click delay (a half second) drives it at the fast speed of 40.
        scene.Seconds(1);
        controller.Process(new MouseButtonEvent(30_100, MouseButton.Right, true, second));
        controller.Process(new MouseButtonEvent(30_150, MouseButton.Right, false, second));
        scene.Seconds(0.1);
        controller.Process(new MouseButtonEvent(30_300, MouseButton.Right, true, second));
        controller.Process(new MouseButtonEvent(30_350, MouseButton.Right, false, second));
        scene.Seconds(0.2);
        before = module.BeamPosition;
        scene.Seconds(1);
        var fast = Distance(module.BeamPosition, (float)before.X.ToDecimal(), (float)before.Y.ToDecimal());
        TestHarness.Assert(Math.Abs(fast - 40f) < 2f, $"after a second click the beam covers 40 units a second ({fast})");
        TestHarness.Assert(controller.Input.Selection.Selected.Length == 1, "and the cannon stays selected");
    }
}

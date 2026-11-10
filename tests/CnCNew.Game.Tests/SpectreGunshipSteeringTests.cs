using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The Spectre gunship (the 5-star USA general's power) as the owner plays it, on real data: the owner's playtest of 8 November 2026 found that "the mouse overlay and the mouse icon" did
/// not work and that a new target did not move the fire point along a line. In the original the power's button lays the targeting circle under the pointer; the click makes the gunship, which
/// replaces the selection (<c>SpectreGunshipDeploymentUpdate::initiateIntentToDoSpecialPower</c> ends with <c>selectObject</c>); with it selected the pointer over the map is the uplink
/// cannon's (<c>MSG_DO_SPECIAL_POWER_OVERRIDE_DESTINATION_HINT</c>), a click moves the reticle at once and the gattling's aim point creeps to it, <c>StrafingIncrement</c> a frame, with the
/// strafing smoke along the way; the gunship's two decals (<c>AttackAreaDecal</c>, <c>TargetingReticleDecal</c>) show the area and the reticle.
/// </summary>
internal static class SpectreGunshipSteeringTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(SpectreGunshipSteeringTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP SpectreGunshipSteeringTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The Spectre gunship: the targeting circle, the gunship selected on arrival, the uplink pointer, a click moves the reticle and the aim point creeps to it", () => Gunship(data));
        return harness.Failures;
    }

    private static void Gunship(GameData data)
    {
        var scenario = new DemoScenario("spectre-steer", "spectre-steer", "spectre-steer", 6000, [new ScenarioUnit("centre", "AmericaCommandCenter", 1, -260, -80, 0f)], [], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            PowersReady = true,
            Sciences = ["SCIENCE_SpectreGunshipSolo"],
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        void Frame()
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        for (var frame = 0; frame < 3; frame++) Frame();
        controller.BuildDrawList(1280, 720);

        // The button at the side: the targeting circle under the pointer.
        var power = controller.Powers.FirstOrDefault(button => button.Button.Name.Contains("SpectreGunship", StringComparison.Ordinal));
        TestHarness.Assert(power is not null && power.IsEnabled, $"the bar offers the gunship ({string.Join(", ", controller.Powers.Select(button => button.Button.Name))})");
        TestHarness.Assert(controller.PressPowerButton(controller.Powers.IndexOf(power!) + 1), "the press on the bar is taken");
        Vector3 Ground(Vector2 screen) => live.Camera.TryScreenToGround(screen, out var point) ? point : throw new InvalidOperationException("the point is not on the ground");
        var first = new Vector2(640, 330);
        var firstGround = Ground(first);
        controller.Process(new MouseMoveEvent(10_000, first));
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(controller.RadiusCursorDecal is { } circle && circle.Texture.StartsWith("SCCSpecTarg", StringComparison.OrdinalIgnoreCase) && Math.Abs(circle.Radius - 200f) < 0.01f,
            $"the targeting circle of 200 under the pointer ({controller.RadiusCursorDecal})");
        controller.Process(new MouseButtonEvent(10_100, MouseButton.Left, true, first));
        controller.Process(new MouseButtonEvent(10_150, MouseButton.Left, false, first));

        // The gunship appears and is selected.
        AirModulesShip? ship = null;
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 20 && ship is null; frame++)
        {
            Frame();
            var entity = live.World.Entities.FirstOrDefault(value => value.Template.Name == "AmericaJetSpectreGunship");
            if (entity is not null) ship = new AirModulesShip(entity.Id, entity.Modules.OfType<SpectreGunshipUpdate>().Single());
        }
        TestHarness.Assert(ship is not null, "the click made a gunship");
        var gunship = ship!.Module;
        Frame();
        TestHarness.Assert(controller.Input.Selection.Selected.Length == 1 && controller.Input.Selection.Selected[0] == ship.Id, $"the gunship replaces the selection ({controller.Input.Selection.Selected.Length} selected)");
        TestHarness.Assert(Math.Abs((float)gunship.InitialTarget.X.ToDecimal() - firstGround.X) < 1f && Math.Abs((float)gunship.InitialTarget.Y.ToDecimal() - firstGround.Y) < 1f, "it flies to the place that was clicked");

        // It inserts and orbits; the area and the reticle are drawn for the owner.
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 120 && gunship.Status != SpectreGunshipUpdate.GunshipStatus.Orbiting; frame++) Frame();
        TestHarness.Assert(gunship.Status == SpectreGunshipUpdate.GunshipStatus.Orbiting, $"the gunship reaches its orbit ({gunship.Status})");
        for (var frame = 0; frame < 4; frame++) Frame();
        var decals = live.Fx.Capture().Decals.Where(decal => decal.Texture.StartsWith("SCCSpec", StringComparison.OrdinalIgnoreCase)).ToArray();
        TestHarness.Assert(decals.Any(decal => decal.Texture.StartsWith("SCCSpecTarg", StringComparison.OrdinalIgnoreCase) && Math.Abs(decal.Radius - 200f) < 0.01f
                && Math.Abs(decal.Position.X - firstGround.X) < 1f && Math.Abs(decal.Position.Y - firstGround.Y) < 1f),
            $"the attack area of 200 lies around the first target ({string.Join("; ", decals)})");
        TestHarness.Assert(decals.Any(decal => decal.Texture.StartsWith("SCCSpecRet", StringComparison.OrdinalIgnoreCase) && Math.Abs(decal.Radius - 25f) < 0.01f
                && Math.Abs(decal.Position.X - firstGround.X) < 1f && Math.Abs(decal.Position.Y - firstGround.Y) < 1f),
            $"the reticle of 25 lies on it ({string.Join("; ", decals)})");

        // The pointer over the map is the uplink cannon's, over an object as well.
        var second = new Vector2(640, 520);
        var secondGround = Ground(second);
        var apart = Vector2.Distance(new Vector2(firstGround.X, firstGround.Y), new Vector2(secondGround.X, secondGround.Y));
        TestHarness.Assert(apart > 90f && apart < 170f, $"setup: the second place is {apart} units from the first, inside the attack area");
        controller.Process(new MouseMoveEvent(20_000, second));
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(controller.Cursor.Kind == CursorKind.ParticleUplinkCannon, $"the pointer with the gunship selected: the uplink pointer ({controller.Cursor.Kind})");
        TestHarness.Assert(controller.RadiusCursorDecal is null, "no targeting circle follows the pointer any more");

        // A click moves the reticle at once and the aim point creeps to it along the ground.
        var feedback = new List<string>();
        controller.CommandFeedbackObserved += value => feedback.Add(value.ToString());
        controller.Process(new MouseButtonEvent(20_100, MouseButton.Right, true, second));
        controller.Process(new MouseButtonEvent(20_150, MouseButton.Right, false, second));
        for (var frame = 0; frame < 12 && Math.Abs((float)gunship.OverrideTarget.Y.ToDecimal() - secondGround.Y) > 1f; frame++) Frame();
        TestHarness.Assert(Math.Abs((float)gunship.OverrideTarget.X.ToDecimal() - secondGround.X) < 1f && Math.Abs((float)gunship.OverrideTarget.Y.ToDecimal() - secondGround.Y) < 1f,
            $"the reticle is where the click was ({gunship.OverrideTarget}; wanted {secondGround}; first {firstGround}; selected {controller.Input.Selection.Selected.Length}; mode {controller.Input.Commands.Mode}; ship {gunship.Status}; feedback {string.Join(" | ", feedback)})");
        Frame();
        decals = [.. live.Fx.Capture().Decals.Where(decal => decal.Texture.StartsWith("SCCSpecRet", StringComparison.OrdinalIgnoreCase))];
        TestHarness.Assert(decals.Length == 1 && Math.Abs(decals[0].Position.X - secondGround.X) < 1f && Math.Abs(decals[0].Position.Y - secondGround.Y) < 1f,
            $"the drawn reticle moved with it ({string.Join("; ", decals)})");
        var path = new List<Vector2>();
        var arrived = -1;
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 12 && arrived < 0; frame++)
        {
            Frame();
            var aim = new Vector2((float)gunship.GattlingTarget.X.ToDecimal(), (float)gunship.GattlingTarget.Y.ToDecimal());
            path.Add(aim);
            if (Vector2.Distance(aim, new Vector2(secondGround.X, secondGround.Y)) < 1f) arrived = frame;
        }
        TestHarness.Assert(arrived >= 0, $"the aim point reached the new place ({path.LastOrDefault()} against {secondGround})");
        var longest = 0f;
        for (var step = 1; step < path.Count; step++) longest = Math.Max(longest, Vector2.Distance(path[step - 1], path[step]));
        // A drawn frame can end between two steps of the increment's shares (60 and 120 Hz), so it may hold a share of one frame and a share of the next; the exact stepping is the simulation test's.
        TestHarness.Assert(longest <= 40.01f, $"it crept: no frame moved it more than two strafing increments of 20 ({longest}; steps {string.Join(" ", path.Zip(path.Skip(1), Vector2.Distance).Select(value => value.ToString("0.0")))})");
        var toGoal = path.Select(point => Vector2.Distance(point, new Vector2(secondGround.X, secondGround.Y))).ToArray();
        TestHarness.Assert(toGoal.Zip(toGoal.Skip(1), (before, after) => after <= before + 0.01f).All(closer => closer), "it came closer in every frame, along the line");
        TestHarness.Assert(path.Count(point => Vector2.Distance(point, new Vector2(firstGround.X, firstGround.Y)) > 5f && Vector2.Distance(point, new Vector2(secondGround.X, secondGround.Y)) > 5f) >= 2,
            "it was seen between the two places, not only at their ends");
    }

    private sealed record AirModulesShip(CnCNew.Simulation.Ids.EntityId Id, SpectreGunshipUpdate Module);
}

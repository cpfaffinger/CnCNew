using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Numerics;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The radius cursor (<c>InGameUI::setRadiusCursor</c>): the guard button lays the shield of <c>GuardAreaRadiusCursor</c> under the pointer, as wide as the unit's guard range, and a special power
/// its own (<c>A10StrikeRadiusCursor</c>, the power's <c>RadiusCursorRadius</c>); the power's button on the bar at the side enters its targeting mode with the readiest carrier. The owner's playtest
/// of 3 November 2026.
/// </summary>
internal static class RadiusCursorTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(RadiusCursorTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP RadiusCursorTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Guarding lays the guard shield under the pointer, as wide as the guard range", () => Guard(data));
        harness.Run("A special power from the bar at the side enters its mode and lays its radius under the pointer", () => Power(data));
        return harness.Failures;
    }

    private static (LiveScenario Live, MatchController Controller) Scene(GameData data, DemoScenario scenario)
    {
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default);
        for (var frame = 0; frame < 3; frame++)
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        controller.Process(new MouseMoveEvent(10_000, new Vector2(640, 380)));
        return (live, controller);
    }

    private static void Guard(GameData data)
    {
        var scenario = new DemoScenario("radius-guard", "radius-guard", "radius-guard", 600, [new ScenarioUnit("tank", "AmericaTankCrusader", 1, 0, 0, 0f)], [], 0f, 0f);
        var (live, controller) = Scene(data, scenario);
        var tank = live.World.FindEntity(live.Tags["tank"])!;
        controller.Input.Selection.Replace([tank.Id]);
        TestHarness.Assert(controller.Input.CommandBar!.Catalog.TryGetButton("Command_Guard", out var guard), "the guard button exists");
        controller.Input.Commands.Activate(live.World, guard!, [tank]);
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var decal = controller.RadiusCursorDecal;
        var range = FixedConvert.ToSingle(live.World.StdGuardRange(tank));
        TestHarness.Assert(decal is { } shown && shown.Texture.Equals("SCCGuard_Generic.tga", StringComparison.OrdinalIgnoreCase) && Math.Abs(shown.Radius - range) < 0.01f && shown.Color.W > 0f,
            $"the guard shield under the pointer, {range} wide ({decal})");
        controller.Input.Commands.Cancel();
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(controller.RadiusCursorDecal is null, "no shield outside the mode");
    }

    private static void Power(GameData data)
    {
        var scenario = new DemoScenario("radius-power", "radius-power", "radius-power", 600, [new ScenarioUnit("centre", "AmericaCommandCenter", 1, -260, -80, 0f)], [], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            PowersReady = true,
            Sciences = ["SCIENCE_A10ThunderboltMissileStrike1"],
        };
        var (live, controller) = Scene(data, scenario);
        controller.BuildDrawList(1280, 720);
        var powers = controller.Powers;
        var a10 = powers.FirstOrDefault(button => button.Button.Name.Contains("A10", StringComparison.Ordinal));
        TestHarness.Assert(a10 is not null && a10.IsEnabled, $"the bar offers the A-10 strike ({string.Join(", ", powers.Select(button => button.Button.Name))})");
        TestHarness.Assert(controller.PressPowerButton(powers.IndexOf(a10!) + 1), "the press on the bar is taken");
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.ButtonTarget, $"the strike waits for its place ({controller.Input.Commands.Mode})");
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var expected = live.World.SpecialPowers.TryGet(a10!.Button.SpecialPower, out var info) ? FixedConvert.ToSingle(info.RadiusCursorRadius) : 0f;
        TestHarness.Assert(controller.RadiusCursorDecal is { } decal && decal.Texture.StartsWith("SCCA10", StringComparison.OrdinalIgnoreCase) && Math.Abs(decal.Radius - expected) < 0.01f,
            $"the strike's radius under the pointer ({controller.RadiusCursorDecal}, {expected}, {a10!.Button.SpecialPower}, {controller.Input.Commands.RadiusCursorType}, {string.Join(",", controller.Input.Commands.PendingSources)})");
        TestHarness.Assert(controller.Input.Commands.Cursor != CursorKind.Normal, $"the pointer changes ({controller.Input.Commands.Cursor})");
    }
}

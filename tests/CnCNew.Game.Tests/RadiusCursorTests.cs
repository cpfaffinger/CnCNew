using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;
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
        harness.Run("The guard mode outlives a member of the group that dies and ends with the last one or a new selection (#381)", () => GuardOutlivesALoss(data));
        harness.Run("A power's target mode outlives a selected unit that dies (#381)", () => PowerOutlivesALoss(data));
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

    private static ulong _damage = 900_000;

    /// <summary>Destroys <paramref name="victim"/> and steps the scene until it has left the selection (the prune before each tick).</summary>
    private static void Kill(LiveScenario live, MatchController controller, Entity victim)
    {
        TestHarness.Assert(live.World.Submit(new DamageCommand(PlayerId.System, ++_damage, live.World.Tick, victim.Id, victim.Body!.MaxHealth * Fixed.FromInt(10), DamageTypes.Unresistable, 0)).Accepted,
            $"{victim.Template.Name} is destroyed");
        for (var frame = 0; frame < 30 && controller.Input.Selection.Contains(victim.Id); frame++)
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        TestHarness.Assert(!controller.Input.Selection.Contains(victim.Id), $"{victim.Template.Name} has left the selection");
    }

    /// <summary>
    /// <c>ControlBar::onDrawableDeselected</c> cancels the pending GUI command only when nothing is left selected, <c>onDrawableSelected</c> whenever something is selected. In the owner's playtest of
    /// 15 November 2026 the guard area under the pointer vanished "on some event" before the click: every member of the group that died or entered a building ended the mode (#381).
    /// </summary>
    private static void GuardOutlivesALoss(GameData data)
    {
        var scenario = new DemoScenario("radius-guard-loss", "radius-guard-loss", "radius-guard-loss", 600,
            [new ScenarioUnit("a", "AmericaTankCrusader", 1, 0, 0, 0f), new ScenarioUnit("b", "AmericaTankCrusader", 1, 40, 0, 0f), new ScenarioUnit("c", "AmericaTankCrusader", 1, 80, 0, 0f),
             new ScenarioUnit("d", "AmericaTankCrusader", 1, 120, 0, 0f)], [], 0f, 0f);
        var (live, controller) = Scene(data, scenario);
        var world = live.World;
        var (a, b, c, d) = (world.FindEntity(live.Tags["a"])!, world.FindEntity(live.Tags["b"])!, world.FindEntity(live.Tags["c"])!, world.FindEntity(live.Tags["d"])!);
        var commands = controller.Input.Commands;
        controller.Input.Selection.Replace([a.Id, b.Id, c.Id]);
        TestHarness.Assert(controller.Input.CommandBar!.Catalog.TryGetButton("Command_Guard", out var guard), "the guard button exists");
        commands.Activate(world, guard!, controller.Input.Selection.OwnSelected(world));
        TestHarness.Assert(commands.Mode == CommandModeKind.Guard, $"the guard mode waits for its place ({commands.Mode})");

        Kill(live, controller, a);
        TestHarness.Assert(commands.Mode == CommandModeKind.Guard, $"a member that dies leaves the guard mode as it was ({commands.Mode})");
        TestHarness.Assert(controller.RadiusCursorDecal is not null, "the guard shield stays under the pointer");
        TestHarness.Assert(commands.PendingSources.SequenceEqual([b.Id, c.Id]), $"the mode acts for the units still selected ({string.Join(",", commands.PendingSources)})");

        // A smaller selection by the person's own hand is no new selection either; one more unit is.
        controller.Input.Selection.Replace([b.Id]);
        TestHarness.Assert(commands.Mode == CommandModeKind.Guard && commands.PendingSources.SequenceEqual([b.Id]), $"a smaller selection keeps the mode ({commands.Mode})");
        controller.Input.Selection.Replace([b.Id, d.Id]);
        TestHarness.Assert(commands.Mode == CommandModeKind.None, $"selecting another unit ends the mode ({commands.Mode})");

        controller.Input.Selection.Replace([b.Id]);
        commands.Activate(world, guard!, controller.Input.Selection.OwnSelected(world));
        Kill(live, controller, b);
        TestHarness.Assert(commands.Mode == CommandModeKind.None, $"the mode ends when nothing is left selected ({commands.Mode})");
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(controller.RadiusCursorDecal is null, "no shield without the mode");

        // The click after a loss guards with the survivor.
        controller.Input.Selection.Replace([c.Id, d.Id]);
        commands.Activate(world, guard!, controller.Input.Selection.OwnSelected(world));
        Kill(live, controller, d);
        TestHarness.Assert(commands.Mode == CommandModeKind.Guard, $"the mode is still armed for the click ({commands.Mode})");
        TestHarness.Assert(commands.CompleteTarget(world, null, new Vector3(0f, 120f, 0f)), "the click guards the place with the unit that is left");
        TestHarness.Assert(commands.Mode == CommandModeKind.None, "the click ends the mode");
    }

    private static void PowerOutlivesALoss(GameData data)
    {
        var scenario = new DemoScenario("radius-power-loss", "radius-power-loss", "radius-power-loss", 600,
            [new ScenarioUnit("centre", "AmericaCommandCenter", 1, -260, -80, 0f), new ScenarioUnit("a", "AmericaTankCrusader", 1, 0, 0, 0f), new ScenarioUnit("b", "AmericaTankCrusader", 1, 40, 0, 0f)],
            [], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            PowersReady = true,
            Sciences = ["SCIENCE_A10ThunderboltMissileStrike1"],
        };
        var (live, controller) = Scene(data, scenario);
        var world = live.World;
        var (a, b) = (world.FindEntity(live.Tags["a"])!, world.FindEntity(live.Tags["b"])!);
        controller.Input.Selection.Replace([a.Id, b.Id]);
        controller.BuildDrawList(1280, 720);
        var powers = controller.Powers;
        var a10 = powers.FirstOrDefault(button => button.Button.Name.Contains("A10", StringComparison.Ordinal));
        TestHarness.Assert(a10 is not null && controller.PressPowerButton(powers.IndexOf(a10) + 1), "the strike is armed from the bar at the side");
        var commands = controller.Input.Commands;
        TestHarness.Assert(commands.Mode == CommandModeKind.ButtonTarget, $"the strike waits for its place ({commands.Mode})");
        Kill(live, controller, a);
        TestHarness.Assert(commands.Mode == CommandModeKind.ButtonTarget && commands.PendingButton == a10!.Button, $"a selected unit that dies leaves the strike's target mode as it was ({commands.Mode})");
        TestHarness.Assert(controller.RadiusCursorDecal is { } decal && decal.Texture.StartsWith("SCCA10", StringComparison.OrdinalIgnoreCase), "the strike's radius stays under the pointer");
    }
}

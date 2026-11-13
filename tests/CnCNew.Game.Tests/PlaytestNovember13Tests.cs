using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>What the owner's playtest of 13 November 2026 found, each pinned on real data.</summary>
internal static class PlaytestNovember13Tests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(PlaytestNovember13Tests));
        harness.Run("The rally line runs from the door over the natural rally point to the rally point, and round the building's corners when the point lies behind it (issue 306)", RallyLineShape);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP PlaytestNovember13Tests on real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A selected own barracks with a rally point shows the line to it in the waypoint colour; an enemy's barracks and one without a rally point show none (issue 306)", () => RallyLineOnScreen(data));
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------ #306 the rally line

    private static void RallyLineShape()
    {
        // A building at the origin, heading 0, 60 x 40 units; its door on the east side.
        var center = Vector3.Zero;
        var exit = new Vector3(30, 0, 0);
        var natural = new Vector3(60, 0, 0);

        var ahead = RallyLine.Compute(exit, natural, new Vector3(300, 50, 0), center, 0f, 60f, 40f);
        TestHarness.Assert(ahead.Points.SequenceEqual([exit, natural, new Vector3(300, 50, 0)]), $"a point ahead of the door: door, natural point, rally point ({string.Join(" ", ahead.Points)})");
        TestHarness.Assert(ahead.Nodes.SequenceEqual([natural]), "one node at the natural rally point");

        var beside = RallyLine.Compute(exit, natural, new Vector3(0, 300, 0), center, 0f, 60f, 40f);
        TestHarness.Assert(beside.Points.Length == 4 && beside.Points[2] == new Vector3(60, 40, 0), $"a point beside the building goes round the door-side corner ({string.Join(" ", beside.Points)})");
        TestHarness.Assert(beside.Nodes.Length == 2, "with a node at the corner and one at the natural point");

        var behind = RallyLine.Compute(exit, natural, new Vector3(-300, 10, 0), center, 0f, 60f, 40f);
        TestHarness.Assert(behind.Points.Length == 5 && behind.Points[2] == new Vector3(60, 40, 0) && behind.Points[3] == new Vector3(-60, 40, 0),
            $"a point behind the building goes round both corners of the near side ({string.Join(" ", behind.Points)})");

        var helipad = RallyLine.Compute(natural, natural, new Vector3(-300, 10, 0), center, 0f, 60f, 40f);
        TestHarness.Assert(helipad.Points.SequenceEqual([natural, new Vector3(-300, 10, 0)]), $"where door and natural point are one the line runs straight ({string.Join(" ", helipad.Points)})");
    }

    private static void RallyLineOnScreen(GameData data)
    {
        var scenario = new DemoScenario("rally-line", "rally-line", "rally-line", 6000,
            [new ScenarioUnit("own", "AmericaBarracks", 1, 0, 0, 0f), new ScenarioUnit("bare", "AmericaBarracks", 1, 0, -140, 0f), new ScenarioUnit("enemy", "ChinaBarracks", 2, 0, 160, 0f)], [], 100f, 0f, CameraHeight: 330f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, SkinPins.Settings) { StarBlinks = false };
        void Frame(int count = 1)
        {
            for (var frame = 0; frame < count; frame++)
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }
        void Rally(string tag, PlayerId player, float x, float y)
            => TestHarness.Assert(live.Issue(player, (sequence, tick) => new SetRallyPointCommand(player, sequence, tick, live.Tags[tag],
                Fixed.FromDecimal((decimal)(live.Anchor.X + x)), Fixed.FromDecimal((decimal)(live.Anchor.Y + y)))).Accepted, $"{tag} takes a rally point");
        Frame(2);
        Rally("own", new PlayerId(1), 220, 30);
        Rally("enemy", new PlayerId(2), 220, 190);
        Frame(3);

        controller.Input.Selection.Replace([live.Tags["own"]]);
        Frame();
        var lines = controller.Overlay.RallyLines;
        TestHarness.Assert(!lines.IsDefaultOrEmpty && lines.Length == 1 && lines[0].Producer == live.Tags["own"], $"the own barracks shows one rally line ({(lines.IsDefault ? 0 : lines.Length)})");
        var line = lines[0];
        var flag = controller.Overlay.RallyPoints.Single(marker => marker.Producer == live.Tags["own"]).Screen;
        TestHarness.Assert(line.Points.Length >= 3 && Vector2.Distance(line.Points[^1], flag) < 1f, $"from the door over the natural point to the flag ({string.Join(" ", line.Points)}, flag {flag})");
        TestHarness.Assert(line.Nodes.Length >= 1, "with the node at the natural rally point");
        var commands = HudOverlayRenderer.Build(controller.Overlay, 0);
        TestHarness.Assert(commands.OfType<UiLineDraw>().Count(draw => draw.Color == new CnCNew.Ui.Wnd.WndColor(64, 128, 255, 235)) >= line.Points.Length - 1, "the line is drawn in the waypoint colour");

        // Waypoint mode draws waypoints, not the rally line (W3DWaypointBuffer::drawWaypoints).
        controller.Input.Commands.OnMeta(live.World, MetaAction.BeginWaypoints);
        Frame();
        TestHarness.Assert(controller.Overlay.RallyLines.IsDefaultOrEmpty, "in waypoint mode no rally line is shown");
        controller.Input.Commands.OnMeta(live.World, MetaAction.EndWaypoints);

        controller.Input.Selection.Replace([live.Tags["bare"]]);
        Frame();
        TestHarness.Assert(controller.Overlay.RallyLines.IsDefaultOrEmpty, "a barracks without a rally point shows no line");
        controller.Input.Selection.Replace([live.Tags["enemy"]]);
        Frame();
        TestHarness.Assert(controller.Overlay.RallyLines.IsDefaultOrEmpty, "an enemy's barracks shows no line");
        controller.Input.Selection.Replace([]);
        Frame();
        TestHarness.Assert(controller.Overlay.RallyLines.IsDefaultOrEmpty, "nothing selected, no line");
    }
}

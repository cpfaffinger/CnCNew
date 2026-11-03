using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Simulation.Commands;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The option "Retaliation" (<c>OptionsMenu.wnd:Retaliation</c>, <c>MSG_ENABLE_RETALIATION_MODE</c>): with the option on, as it ships, a match issues nothing; switched off, the client posts one
/// command within a second of the original (<c>Player::update</c>) and the player's mode in the world follows; switched on again, one more.
/// </summary>
internal static class RetaliationOptionTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(RetaliationOptionTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP RetaliationOptionTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The option Retaliation reaches the player's mode within a second, and only when it differs", () => Option(data));
        return harness.Failures;
    }

    private static void Option(GameData data)
    {
        var scenario = DemoScenarios.Find(FactoryExits.NameOf(FactoryExits.Factions[0], "all")) ?? throw new InvalidOperationException("no factory scene");
        var live = LiveScenario.Create(data, scenario with { Orders = [] }, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default);
        var issued = new List<SetRetaliationCommand>();
        live.CommandIssued += (command, result) =>
        {
            if (command is SetRetaliationCommand retaliation && result.Accepted) issued.Add(retaliation);
        };
        void Advance(int frames)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }

        Advance(90);
        TestHarness.Assert(issued.Count == 0 && live.World.RetaliationEnabled(live.Human), $"With the option on nothing is issued ({issued.Count}).");
        controller.Retaliation = false;
        Advance(45);
        TestHarness.Assert(issued.Count == 1 && !issued[0].Enabled && !live.World.RetaliationEnabled(live.Human), $"Switched off: one command, and the player's mode is off ({issued.Count}).");
        Advance(90);
        TestHarness.Assert(issued.Count == 1, $"Nothing more while the two agree ({issued.Count}).");
        controller.Retaliation = true;
        Advance(45);
        TestHarness.Assert(issued.Count == 2 && issued[1].Enabled && live.World.RetaliationEnabled(live.Human), $"Switched on again: one more ({issued.Count}).");
    }
}

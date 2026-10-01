using System.Numerics;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
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
        });
        return harness.Failures;
    }
}

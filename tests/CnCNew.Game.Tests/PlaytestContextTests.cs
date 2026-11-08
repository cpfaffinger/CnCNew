using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>What the owner's playtest of 7 November 2026 found with the pointer and the context orders, each pinned on real data.</summary>
internal static class PlaytestContextTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(PlaytestContextTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP PlaytestContextTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Soldiers over an enemy transport get the attack pointer and the attack order, never enter (Humvee, Chinook, Troop Crawler, Helix, Battle Bus, Technical)", () => EnemyTransports(data));
        harness.Run("A Chinook made at the supply center gathers by itself; one that stands idle takes the right click on a supply pile (pointer and order) and gathers", () => ChinookGathers(data));
        return harness.Failures;
    }

    /// <summary>
    /// Finding 14: "My Chinooks as USA do not gather supplies; I also cannot give them a command with the mouse." The Chinook is a supply carrier (<c>ChinookAIUpdate</c> is a
    /// <c>SupplyTruckAIUpdate</c>): the supply center's exit sends the one it makes to gather (<c>SupplyCenterProductionExitUpdate</c>), and <c>canTransferSuppliesAt</c> lets a click on a supply
    /// pile (a warehouse) or an own supply center send a carrier there. The player's money is the measure: the only income in the scenario is a delivery.
    /// </summary>
    private static void ChinookGathers(GameData data)
    {
        // A skirmish on a shipped map, as the owner plays it: the dozer builds a power plant and a supply center near the nearest pile, the center makes a Chinook.
        static void HumanSkirmish(GameData data)
        {
            var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
            var live = LiveMatch.Create(data, new MatchSetup(mapPath, "FactionAmerica", ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard), new Vector2(1280, 720));
            void Run(double seconds, Func<bool>? until = null)
            {
                for (var frame = 0; frame < seconds * CnCNew.Tests.TestTime.FramesPerSecond && until?.Invoke() != true; frame++) live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            }
            Run(2);
            var human = live.Human;
            var world = live.World;
            var center = world.Entities.First(entity => entity.Owner == human && entity.Template.Name == "AmericaCommandCenter");
            var dozer = world.Entities.First(entity => entity.Owner == human && entity.Template.Name == "AmericaVehicleDozer");
            var piles = world.Entities.Where(entity => entity.Template.IsKindOf("SUPPLY_SOURCE")).OrderBy(entity => (entity.X - center.X).Abs() + (entity.Y - center.Y).Abs()).ToArray();
            TestHarness.Assert(piles.Length > 0, "the map has supply piles");
            SimCommand? Site(string template, Entity around, int from, int to)
            {
                for (var radius = from; radius <= to; radius += 20)
                    for (var step = 0; step < 16; step++)
                    {
                        var angle = step * Math.PI / 8;
                        var (x, y) = (around.X + Fixed.FromInt((int)(radius * Math.Cos(angle))), around.Y + Fixed.FromInt((int)(radius * Math.Sin(angle))));
                        var command = new ConstructCommand(human, 0, 0, dozer.Id, template, x, y, Fixed.Zero);
                        if (world.Check(command).Accepted) return command;
                    }
                return null;
            }
            var plant = Site("AmericaPowerPlant", center, 100, 300);
            TestHarness.Assert(plant is ConstructCommand, "a free place for the power plant");
            var plantOrder = (ConstructCommand)plant!;
            TestHarness.Assert(live.Commands.Issue((sequence, tick) => plantOrder with { Sequence = sequence, Tick = tick }).Accepted, "the power plant is ordered");
            Run(120, () => world.Entities.Any(entity => entity.Owner == human && entity.Template.Name == "AmericaPowerPlant" && !entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction)));
            TestHarness.Assert(world.Entities.Any(entity => entity.Owner == human && entity.Template.Name == "AmericaPowerPlant" && !entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction)), "the power plant stands");
            var stash = Site("AmericaSupplyCenter", piles[0], 120, 300);
            TestHarness.Assert(stash is ConstructCommand, "a free place for the supply center by the pile");
            var stashOrder = (ConstructCommand)stash!;
            TestHarness.Assert(live.Commands.Issue((sequence, tick) => stashOrder with { Sequence = sequence, Tick = tick }).Accepted, "the supply center is ordered");
            bool Finished() => world.Entities.Any(entity => entity.Owner == human && entity.Template.Name == "AmericaSupplyCenter" && !entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction) && entity.Z >= Fixed.Zero && (entity.X - stashOrder.X).Abs() < Fixed.FromInt(40));
            Run(240, Finished);
            TestHarness.Assert(Finished(), "the supply center stands");
            var finished = world.Entities.First(entity => entity.Owner == human && entity.Template.Name == "AmericaSupplyCenter" && !entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction) && (entity.X - stashOrder.X).Abs() < Fixed.FromInt(40));
            // The finished center brings its own Chinook (SpawnBehavior); nothing is queued here, and the only income there is is a delivery.
            Run(5);
            TestHarness.Assert(world.Entities.Count(entity => entity.Owner == human && entity.Template.Name == "AmericaVehicleChinook") == 1, "the finished center spawned its Chinook");
            var money = world.MoneyOf(human);
            var gained = false;
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 240 && !gained; frame++)
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                var now = world.MoneyOf(human);
                gained = now > money;
                money = now;
            }
            TestHarness.Assert(gained, "the Chinook the human's own finished supply center brought delivered supplies within four minutes");
        }

        // The player's own way: the dozer builds the supply center beside the pile, then the center makes a Chinook.
        static void BuiltCenter(GameData data)
        {
            var scenario = new DemoScenario("ctx-built", "ctx-built", "ctx-built", 9000,
                [
                    new ScenarioUnit("dozer", "AmericaVehicleDozer", 1, 0, 0, 0f),
                    new ScenarioUnit("power", "AmericaPowerPlant", 1, -200, -150, 0f),
                    new ScenarioUnit("pile", "SupplyPile", 0, 260, 60, 0f),
                ],
                [
                    new ScenarioOrder(2, "dozer", ScenarioOrderKind.Build, "center", 90, 20, "AmericaSupplyCenter"),
                    new ScenarioOrder(2400, "center", ScenarioOrderKind.Produce, "made", 0, 0, "AmericaVehicleChinook"),
                ], 130f, 20f)
            {
                HumanFaction = "FactionAmerica",
                OpponentFaction = "FactionChina",
                StartMoney = 3000,
            };
            var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
            var money = live.World.MoneyOf(live.Human);
            long deliveredAt = -1;
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 400 && deliveredAt < 0; frame++)
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                var now = live.World.MoneyOf(live.Human);
                if (now > money && live.Tags.ContainsKey("made")) deliveredAt = live.World.Tick;
                money = now;
            }
            TestHarness.Assert(deliveredAt >= 0, $"the Chinook of the center the dozer built delivered supplies within 400 s (money {money})");
        }

        static void Run(GameData data, string faction, string centerName, string chinookName, bool produce, bool rightClick)
        {
            var scenario = new DemoScenario("ctx-chinook", "ctx-chinook", "ctx-chinook", 9000,
                [
                    new ScenarioUnit("center", centerName, 1, 0, 0, 0f),
                    new ScenarioUnit("pile", "SupplyPile", 0, 230, 40, 0f),
                    new ScenarioUnit("idle", chinookName, 1, -120, -120, 0f),
                ],
                produce ? [new ScenarioOrder(2, "center", ScenarioOrderKind.Produce, "made", 0, 0, chinookName)] : [], 100f, 0f)
            {
                HumanFaction = faction,
                OpponentFaction = "FactionChina",
                StartMoney = 3000,
            };
            var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
            var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
            void Frame()
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond; frame++) Frame();
            var what = (produce ? "the " : "the idle ") + chinookName + (produce ? " the " + centerName + " made" : " after the click");
            if (rightClick)
            {
                controller.Input.Selection.Replace([live.Tags["idle"]]);
                var pile = live.World.FindEntity(live.Tags["pile"])!;
                var point = new Vector3(CnCNew.Presentation.FixedConvert.ToSingle(pile.X), CnCNew.Presentation.FixedConvert.ToSingle(pile.Y), CnCNew.Presentation.FixedConvert.ToSingle(pile.Z) + 5f);
                TestHarness.Assert(live.Camera.TryWorldToScreen(point, out var at), "the supply pile is on the screen");
                controller.Process(new MouseMoveEvent(5_000, at));
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
                TestHarness.Assert(controller.Cursor.Kind == CursorKind.Dock, $"a Chinook over a supply pile: the dock pointer ({controller.Cursor.Kind})");
                controller.Process(new MouseButtonEvent(6_010, MouseButton.Right, true, at));
                controller.Process(new MouseButtonEvent(6_060, MouseButton.Right, false, at));
            }
            var money = live.World.MoneyOf(live.Human);
            long gathered = 0;
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 150 && gathered == 0; frame++)
            {
                Frame();
                var now = live.World.MoneyOf(live.Human);
                if (now > money) gathered = (long)(now - money).Floor();
                money = now;
            }
            TestHarness.Assert(gathered > 0, $"{what} delivered supplies within 150 s (money {money})");
        }
        BuiltCenter(data);
        HumanSkirmish(data);
        foreach (var (faction, center, chinook) in new[]
                 {
                     ("FactionAmerica", "AmericaSupplyCenter", "AmericaVehicleChinook"),
                     ("FactionAmericaAirForceGeneral", "AirF_AmericaSupplyCenter", "AFG_AmericaVehicleChinook"),
                     ("FactionAmericaLaserGeneral", "Lazr_AmericaSupplyCenter", "Lazr_AmericaVehicleChinook"),
                     ("FactionAmericaSuperWeaponGeneral", "SupW_AmericaSupplyCenter", "SupW_AmericaVehicleChinook"),
                 })
        {
            Run(data, faction, center, chinook, produce: true, rightClick: false);
            Run(data, faction, center, chinook, produce: false, rightClick: true);
        }
    }

    /// <summary>
    /// Finding 7: with several soldiers selected the pointer over an enemy troop carrier said "enter", and the soldiers tried to climb in. <c>TransportContain::isValidContainerFor</c> accepts only
    /// the transport's own player's units, so <c>canEnterObject</c> is false and the order of the original's <c>evaluateContextCommand</c> goes on to the attack. An own transport and a civilian
    /// building keep the enter pointer, also for a selection some of whom cannot enter.
    /// </summary>
    private static void EnemyTransports(GameData data)
    {
        Run(data, "FactionAmerica", "FactionChina", "AmericaInfantryRanger", "AmericaTankCrusader", ["ChinaVehicleTroopCrawler", "ChinaVehicleHelix"], ["AmericaVehicleHumvee"]);
        Run(data, "FactionChina", "FactionAmerica", "ChinaInfantryRedguard", "ChinaTankBattleMaster", ["AmericaVehicleHumvee", "AmericaVehicleChinook"], ["ChinaVehicleTroopCrawler"]);
        Run(data, "FactionChina", "FactionGLA", "ChinaInfantryRedguard", "ChinaTankBattleMaster", ["GLAVehicleBattleBus", "GLAVehicleTechnicalChassisOne"], ["ChinaVehicleTroopCrawler"]);

        static string Describe(LiveScenario live, MatchController controller, Vector2 screen)
            => controller.Input.Selection.Picker.Pick(live.World, live.Human, live.Camera, screen) is { } picked ? picked.Template.Name + "/" + picked.Owner.Value : "nothing";

        static void Run(GameData data, string human, string opponent, string soldier, string tank, string[] enemies, string[] own)
        {
            var units = new List<ScenarioUnit>
            {
                new("s1", soldier, 1, 0, 0), new("s2", soldier, 1, 0, 25), new("s3", soldier, 1, 0, 50), new("tank", tank, 1, 0, 90),
                new("house", "GermanTownHouse1", 0, -110, 0),
            };
            for (var i = 0; i < enemies.Length; i++) units.Add(new ScenarioUnit("enemy" + i, enemies[i], 2, 130, -60 + 70 * i));
            for (var i = 0; i < own.Length; i++) units.Add(new ScenarioUnit("own" + i, own[i], 1, -110, 70 + 70 * i));
            var scenario = new DemoScenario("ctx-enter", "ctx-enter", "ctx-enter", 4500, [..units], [], 0f, 20f) { HumanFaction = human, OpponentFaction = opponent };
            var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
            var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
            void Frame()
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond; frame++) Frame();
            Vector2 ScreenOf(string tag)
            {
                var entity = live.World.FindEntity(live.Tags[tag])!;
                var point = new Vector3(CnCNew.Presentation.FixedConvert.ToSingle(entity.X), CnCNew.Presentation.FixedConvert.ToSingle(entity.Y), CnCNew.Presentation.FixedConvert.ToSingle(entity.Z) + 5f);
                TestHarness.Assert(live.Camera.TryWorldToScreen(point, out var screen), $"the {tag} is on the screen");
                return screen;
            }
            CursorKind Over(string tag)
            {
                controller.Process(new MouseMoveEvent(5_000, ScreenOf(tag)));
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
                return controller.Cursor.Kind;
            }
            string[] soldiers = ["s1", "s2", "s3"];
            var tagsOf = new Func<string[], ImmutableArray<CnCNew.Simulation.Ids.EntityId>>(names => [..names.Select(name => live.Tags[name])]);

            controller.Input.Selection.Replace(tagsOf(soldiers));
            for (var i = 0; i < own.Length; i++)
                TestHarness.Assert(Over("own" + i) == CursorKind.EnterFriendly, $"{human}: over an own {own[i]}: enter ({controller.Cursor.Kind})");
            TestHarness.Assert(Over("house") == CursorKind.EnterFriendly, $"{human}: over the civilian house: enter ({controller.Cursor.Kind})");
            controller.Input.Selection.Replace(tagsOf([..soldiers, "tank"]));
            TestHarness.Assert(Over("own0") == CursorKind.EnterFriendly, $"{human}: a mixed selection over an own {own[0]}: enter ({controller.Cursor.Kind})");

            for (var i = 0; i < enemies.Length; i++)
            {
                foreach (var selection in new[] { soldiers, [..soldiers, "tank"] })
                {
                    controller.Input.Selection.Replace(tagsOf(selection));
                    var cursor = Over("enemy" + i);
                    TestHarness.Assert(cursor is CursorKind.AttackObj or CursorKind.OutRange, $"{human}: {selection.Length} units over the enemy {enemies[i]}: attack, never enter ({cursor}; the pick is {Describe(live, controller, ScreenOf("enemy" + i))}, the unit is seen {live.World.CanPlayerSee(live.Human, live.World.FindEntity(live.Tags["enemy" + i])!)})");
                }
            }

            // The click gives the attack order and nobody gets in, however long the soldiers walk about.
            controller.Input.Selection.Replace(tagsOf(soldiers));
            for (var i = 0; i < enemies.Length; i++)
            {
                var at = ScreenOf("enemy" + i);
                controller.Process(new MouseMoveEvent(6_000 + 1000 * i, at));
                controller.Process(new MouseButtonEvent(6_010 + 1000 * i, MouseButton.Right, true, at));
                controller.Process(new MouseButtonEvent(6_060 + 1000 * i, MouseButton.Right, false, at));
                var feedback = controller.Input.Commands.DrainFeedback().Select(item => item.Kind).ToArray();
                TestHarness.Assert(feedback.Contains(FeedbackKind.AttackMarker) && !feedback.Contains(FeedbackKind.EnterMarker), $"{human}: the right click on the enemy {enemies[i]} is an attack ({string.Join(",", feedback)})");
                TestHarness.Assert(soldiers.All(tag => controller.Input.Commands.Orders.TryGet(live.Tags[tag], out var order) && order.Kind == StandingOrderKind.Attack && order.Target == live.Tags["enemy" + i]),
                    $"{human}: every soldier has the standing attack order on the enemy {enemies[i]}");
            }
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 25; frame++)
            {
                Frame();
                foreach (var tag in soldiers)
                    if (live.World.FindEntity(live.Tags[tag]) is { } soldierEntity && soldierEntity.ContainedBy.IsValid)
                        TestHarness.Assert(live.World.FindEntity(soldierEntity.ContainedBy) is { } carrier && carrier.Owner.Value != 2, $"{human}: {tag} climbed into an enemy {live.World.FindEntity(soldierEntity.ContainedBy)?.Template.Name}");
            }
        }
    }
}

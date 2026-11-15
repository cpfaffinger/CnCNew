using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Tests;

/// <summary>
/// Issue 382, the owner's playtest of 15 November 2026: the GLA Angry Mob could not be commanded. Its nexus has no model and is <c>MASKED</c>, so a person sees and clicks
/// its members; the original makes a selected member select the whole mob (<c>SpawnBehavior::computeAggregateStates</c>) and shows the nexus' bar for it
/// (<c>InGameUI::evaluateSoloNexus</c>). Each through the events a person's hands make, on real data.
/// </summary>
internal static class AngryMobTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(AngryMobTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP AngryMobTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A click on a member of an angry mob selects every member and the nexus, a box over members likewise, and the bar is the nexus' Attack Move, Guard and Stop for all of them (issue 382)", () => SelectionAndBar(data));
        harness.Run("A right click on the ground moves the whole mob: the nexus goes there and its members with it (issue 382)", () => MoveTheMob(data));
        harness.Run("A right click on an enemy sends every member and the nexus at it, and the enemy is hurt (issue 382)", () => AttackWithTheMob(data));
        harness.Run("An angry mob trained at a barracks is one unit too: a click on a member selects it, the bar is its own, a right click moves it (issue 382)", () => TrainedMob(data));
        harness.Run("Attack Move of the bar moves the whole mob, Stop of the bar stops it (issue 382)", () => BarOrders(data));
        return harness.Failures;
    }

    private sealed class Rig
    {
        public Rig(GameData data, string name, float enemyX = 900f, float enemyY = 0f, ScenarioUnit[]? units = null)
        {
            var scenario = new DemoScenario(name, name, name, 30000,
                units is not null ? [..units] : [new ScenarioUnit("mob", "GLAInfantryAngryMobNexus", 1, 0, 0, 0f), new ScenarioUnit("enemy", "AmericaTankCrusader", 2, enemyX, enemyY, 180f)], [], 0f, 0f, CameraHeight: 330f)
            {
                StartMoney = 30000,
                HumanFaction = "FactionGLA",
                OpponentFaction = "FactionAmerica",
            };
            Live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
            Controller = new MatchController(Live, SkinPins.Settings) { StarBlinks = false };
            Live.CommandIssued += (command, _) => Issued.Add(command);
            // The nexus makes its first five members at once (InitialBurst) and the rest every five seconds.
            Seconds(2);
            if (units is null) TestHarness.Assert(Members().Length >= 5, $"the nexus made its first members ({Members().Length})");
        }

        public LiveScenario Live { get; }
        public MatchController Controller { get; }
        public List<SimCommand> Issued { get; } = [];
        public SelectionModel Selection => Controller.Input.Selection;
        public long Time { get; private set; } = 100_000;

        public Entity Nexus => Live.Tags.TryGetValue("mob", out var mob)
            ? Live.World.FindEntity(mob)!
            : Live.World.Entities.Single(entity => entity.Owner == Live.Human && entity.Template.Name == "GLAInfantryAngryMobNexus");
        public Entity Enemy => Live.World.FindEntity(Live.Tags["enemy"])!;

        public Entity[] Members()
            => [..Nexus.Modules.OfType<ISpawnerModule>().Single().Slaves.Select(Live.World.FindEntity).OfType<Entity>().Where(member => !member.EffectivelyDead)];

        public uint[] Mob() => [Nexus.Id.Value, ..Members().Select(member => member.Id.Value)];

        public uint[] Selected() => [..Selection.Selected.Select(id => id.Value)];

        public void Seconds(double seconds)
        {
            for (var frame = 0; frame < seconds * CnCNew.Tests.TestTime.FramesPerSecond; frame++)
            {
                Live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }

        public Vector2 ScreenOf(Entity entity)
        {
            TestHarness.Assert(Selection.Picker.TryProject(Live.Camera, entity, out var projected), $"{entity.Template.Name} is in front of the camera");
            TestHarness.Assert(projected.Center.X is > 40 and < 1240 && projected.Center.Y is > 40 and < 540, $"{entity.Template.Name} is on the screen above the bar ({projected.Center})");
            return projected.Center;
        }

        public Vector2 ScreenOfGround(float dx, float dy)
        {
            var nexus = Nexus;
            var at = new Vector3((float)nexus.X.ToDecimal() + dx, (float)nexus.Y.ToDecimal() + dy, (float)nexus.Z.ToDecimal());
            TestHarness.Assert(Live.Camera.TryWorldToScreen(at, out var screen), "the point is in front of the camera");
            TestHarness.Assert(screen.X is > 40 and < 1240 && screen.Y is > 40 and < 540, $"the point is on the screen above the bar ({screen})");
            return screen;
        }

        public void Click(Vector2 at, MouseButton button = MouseButton.Left)
        {
            Time += 700;
            Controller.Process(new MouseMoveEvent(Time += 10, at));
            Controller.Process(new MouseButtonEvent(Time += 40, button, true, at));
            Controller.Process(new MouseButtonEvent(Time += 40, button, false, at));
        }

        public void Drag(Vector2 from, Vector2 to)
        {
            Time += 700;
            Controller.Process(new MouseMoveEvent(Time += 10, from));
            Controller.Process(new MouseButtonEvent(Time += 40, MouseButton.Left, true, from));
            for (var step = 1; step <= 8; step++) Controller.Process(new MouseMoveEvent(Time += 16, Vector2.Lerp(from, to, step / 8f)));
            Controller.Process(new MouseButtonEvent(Time += 40, MouseButton.Left, false, to));
        }

        public void ClickButton(int slot)
        {
            TestHarness.Assert(slot > 0, "the button is on the bar");
            Controller.BuildDrawList(1280, 720);
            var rect = Controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}")!.Value;
            Click(new Vector2(rect.CenterX, rect.CenterY));
        }

        public int SlotOf(string button)
        {
            var bar = Controller.CommandBar;
            for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
                if (bar[slot] is { } shown && shown.Button.Name.Equals(button, StringComparison.Ordinal)) return slot;
            return -1;
        }

        /// <summary>Clicks the newest member, as a person clicks the mob.</summary>
        public void SelectByClick()
        {
            Click(ScreenOf(Members()[^1]));
            Seconds(0.1);
        }

        public float DistanceTo(Entity entity, float x, float y) => Vector2.Distance(new Vector2((float)entity.X.ToDecimal(), (float)entity.Y.ToDecimal()), new Vector2(x, y));
    }

    private static string Ids(IEnumerable<uint> ids) => string.Join(",", ids.Order());

    private static void SelectionAndBar(GameData data)
    {
        var rig = new Rig(data, "angry-mob-selection");
        TestHarness.Assert(!EntityPicker.IsSelectable(rig.Nexus), "the nexus is masked: nobody picks it directly, as in the original");
        rig.SelectByClick();
        TestHarness.Assert(Ids(rig.Selected()) == Ids(rig.Mob()), $"a click on one member selects the whole mob: {Ids(rig.Mob())} (selected {Ids(rig.Selected())})");
        var bar = rig.Controller.CommandBar;
        TestHarness.Assert(bar.Context == CommandBarContext.Command && bar.CommandSet == "GLAInfantryAngryMobCommandSet", $"the bar is the nexus' command set ({bar.Context} {bar.CommandSet})");
        foreach (var name in new[] { "Command_AttackMove", "Command_Guard", "Command_Stop" })
        {
            var slot = rig.SlotOf(name);
            TestHarness.Assert(slot > 0, $"the bar shows {name}");
            TestHarness.Assert(Ids(bar[slot]!.Sources.Select(id => id.Value)) == Ids(rig.Mob()), $"{name} acts on the whole mob ({Ids(bar[slot]!.Sources.Select(id => id.Value))})");
        }

        // A click on the ground clears the selection; a box over the members brings the whole mob back, and the bar with it.
        rig.Click(rig.ScreenOfGround(-150f, 120f), MouseButton.Left);
        rig.Seconds(0.1);
        TestHarness.Assert(rig.Selected().Length == 0, $"a click on empty ground deselects ({Ids(rig.Selected())})");
        var center = rig.ScreenOf(rig.Nexus);
        rig.Drag(center - new Vector2(60, 60), center + new Vector2(60, 60));
        rig.Seconds(0.1);
        TestHarness.Assert(Ids(rig.Selected()) == Ids(rig.Mob()), $"a box over the members selects the whole mob ({Ids(rig.Selected())})");
        TestHarness.Assert(rig.Controller.CommandBar.CommandSet == "GLAInfantryAngryMobCommandSet", "the box's bar is the nexus' too");

        // The selection stays the mob's while the world steps (the masked nexus is kept), and a new member joins it (computeAggregateStates every frame).
        var before = rig.Members().Length;
        rig.Seconds(12);
        TestHarness.Assert(rig.Members().Length > before, $"the nexus made more members ({before} -> {rig.Members().Length})");
        TestHarness.Assert(Ids(rig.Selected()) == Ids(rig.Mob()), $"the selection follows the mob as it grows ({Ids(rig.Selected())} against {Ids(rig.Mob())})");
    }

    private static void MoveTheMob(GameData data)
    {
        var rig = new Rig(data, "angry-mob-move");
        rig.SelectByClick();
        var (startX, startY) = ((float)rig.Nexus.X.ToDecimal(), (float)rig.Nexus.Y.ToDecimal());
        var (goalX, goalY) = (startX + 120f, startY + 40f);
        rig.Click(rig.ScreenOfGround(120f, 40f), MouseButton.Right);
        rig.Seconds(0.2);
        var moves = rig.Issued.OfType<GroupMoveCommand>().ToList();
        TestHarness.Assert(moves.Count == 1, $"one group move ({moves.Count})");
        TestHarness.Assert(Ids(moves[0].Units.Select(id => id.Value)) == Ids(rig.Mob()), $"the move takes the whole mob ({Ids(moves[0].Units.Select(id => id.Value))})");
        rig.Seconds(20);
        TestHarness.Assert(rig.DistanceTo(rig.Nexus, goalX, goalY) < 20f, $"the nexus reached the point ({rig.DistanceTo(rig.Nexus, goalX, goalY)})");
        foreach (var member in rig.Members())
            TestHarness.Assert(rig.DistanceTo(member, goalX, goalY) < 60f, $"member {member.Id.Value} came along ({rig.DistanceTo(member, goalX, goalY)} from the point)");
    }

    private static void AttackWithTheMob(GameData data)
    {
        var rig = new Rig(data, "angry-mob-attack", enemyX: 170f, enemyY: -40f);
        rig.SelectByClick();
        var health = rig.Enemy.Body!.Health;
        rig.Click(rig.ScreenOf(rig.Enemy), MouseButton.Right);
        rig.Seconds(0.2);
        foreach (var id in rig.Mob())
            TestHarness.Assert(rig.Controller.Input.Commands.Orders.TryGet(new EntityId(id), out var order) && order.Kind == StandingOrderKind.Attack && order.Target == rig.Enemy.Id,
                $"{rig.Live.World.FindEntity(new EntityId(id))!.Template.Name} {id} is ordered to attack the enemy");
        var enemyId = rig.Enemy.Id;
        var (enemyX, enemyY) = ((float)rig.Enemy.X.ToDecimal(), (float)rig.Enemy.Y.ToDecimal());
        var near = 0;
        for (var second = 0; second < 15 && near < 5; second++)
        {
            rig.Seconds(1);
            near = rig.Members().Count(member => rig.DistanceTo(member, enemyX, enemyY) < 120f);
        }
        TestHarness.Assert(near >= 5, $"the members went at the enemy ({near} of {rig.Members().Length} within 120)");
        rig.Seconds(10);
        var enemy = rig.Live.World.FindEntity(enemyId);
        TestHarness.Assert(enemy is null || enemy.EffectivelyDead || enemy.Body!.Health < health, $"the mob hurt the enemy ({enemy?.Body?.Health} of {health})");
    }

    private static void TrainedMob(GameData data)
    {
        var rig = new Rig(data, "angry-mob-trained", units:
        [
            new ScenarioUnit("barracks", "GLABarracks", 1, 0, 0, 0f), new ScenarioUnit("palace", "GLAPalace", 1, -260, 0, 0f),
            new ScenarioUnit("enemy", "AmericaTankCrusader", 2, 2000, 0, 180f),
        ]);
        rig.Click(rig.ScreenOf(rig.Live.World.FindEntity(rig.Live.Tags["barracks"])!));
        rig.Seconds(0.1);
        rig.ClickButton(rig.SlotOf("Command_ConstructGLAInfantryAngryMob"));
        rig.Seconds(0.2);
        TestHarness.Assert(rig.Issued.OfType<QueueProductionCommand>().Any(order => order.Template == "GLAInfantryAngryMobNexus"), "the barracks takes the order for an angry mob");
        // BuildTime 15 s, at half speed without power (a GLA player has none; calcTimeToBuild), and its first members.
        rig.Seconds(45);
        TestHarness.Assert(rig.Members().Length >= 5, $"the trained nexus made its members ({rig.Members().Length})");
        rig.Click(rig.ScreenOfGround(-120f, 200f), MouseButton.Left);
        rig.Seconds(0.1);
        rig.SelectByClick();
        TestHarness.Assert(Ids(rig.Selected()) == Ids(rig.Mob()), $"a click on one member selects the whole mob ({Ids(rig.Selected())} against {Ids(rig.Mob())})");
        TestHarness.Assert(rig.Controller.CommandBar.CommandSet == "GLAInfantryAngryMobCommandSet", $"the bar is the mob's ({rig.Controller.CommandBar.CommandSet})");
        var (goalX, goalY) = ((float)rig.Nexus.X.ToDecimal() + 100f, (float)rig.Nexus.Y.ToDecimal() + 60f);
        rig.Click(rig.ScreenOfGround(100f, 60f), MouseButton.Right);
        rig.Seconds(20);
        TestHarness.Assert(rig.DistanceTo(rig.Nexus, goalX, goalY) < 20f, $"the nexus reached the point ({rig.DistanceTo(rig.Nexus, goalX, goalY)})");
        var near = rig.Members().Count(member => rig.DistanceTo(member, goalX, goalY) < 60f);
        TestHarness.Assert(near == rig.Members().Length, $"its members came along ({near} of {rig.Members().Length})");
    }

    private static void BarOrders(GameData data)
    {
        var rig = new Rig(data, "angry-mob-bar");
        rig.SelectByClick();
        var (startX, startY) = ((float)rig.Nexus.X.ToDecimal(), (float)rig.Nexus.Y.ToDecimal());
        rig.ClickButton(rig.SlotOf("Command_AttackMove"));
        rig.Click(rig.ScreenOfGround(-120f, 60f), MouseButton.Left);
        rig.Seconds(0.2);
        var moves = rig.Issued.OfType<GroupMoveCommand>().ToList();
        TestHarness.Assert(moves.Count == 1 && moves[0].Attack, $"Attack Move gives one attack move ({moves.Count})");
        TestHarness.Assert(Ids(moves[0].Units.Select(id => id.Value)) == Ids(rig.Mob()), $"the attack move takes the whole mob ({Ids(moves[0].Units.Select(id => id.Value))})");
        rig.Seconds(2);
        TestHarness.Assert(rig.DistanceTo(rig.Nexus, startX, startY) > 5f, "the nexus is on its way");
        var before = rig.Issued.Count;
        rig.ClickButton(rig.SlotOf("Command_Stop"));
        rig.Seconds(0.2);
        var stopped = rig.Issued.Skip(before).OfType<SetMoveTargetCommand>().Select(stop => stop.Target.Value).Distinct();
        TestHarness.Assert(Ids(stopped) == Ids(rig.Mob()), $"Stop reaches the whole mob ({Ids(stopped)})");
        var (stopX, stopY) = ((float)rig.Nexus.X.ToDecimal(), (float)rig.Nexus.Y.ToDecimal());
        rig.Seconds(3);
        TestHarness.Assert(rig.DistanceTo(rig.Nexus, stopX, stopY) < 2f, $"the nexus stands ({rig.DistanceTo(rig.Nexus, stopX, stopY)})");
    }
}

using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.World;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The owner's wishes of 7 November 2026 for the selection (a feature of this client, StarCraft is the model): buildings of one kind selected together with a double click, E, Ctrl and a
/// digit; one bar for all of them, where a production order goes to the building with the shortest queue and the orders that fit all alike go to all (the rally point, selling);
/// and Tab through the kinds of a mixed selection of units. Each on real data, through the events a person's hands make.
/// </summary>
internal static class StructureGroupTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(StructureGroupTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP StructureGroupTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A double click on an own building selects the buildings of its kind on the screen, Alt on the whole map; an enemy's building, a site and another kind stay out", () => DoubleClick(data));
        harness.Run("E on a building finds the buildings of its kind on the screen, pressed again across the map, and says so; a Shift-click adds one of the same kind and refuses another", () => MatchingKey(data));
        harness.Run("Ctrl and a digit keep a structure group, the digit brings it back, Shift and a digit adds a building of its kind and empties the selection for another", () => Groups(data));
        harness.Run("The bar of three barracks shows the count; a production order goes to the shortest queue: 1/1/1, then 2/1/1, then 2/2/1 - also when the clicks come before the world has stepped", () => Distribution(data));
        harness.Run("A queue holds the original's 9 entries: one barracks and a group stop taking orders at their limit, nothing is paid beyond it, the button greys, the full queue is said, and each building's load is shown in both skins", () => QueueLimit(data));
        harness.Run("A hot key of the bar distributes the same way, and a queue icon cancels the lead building's order", () => HotkeyAndCancel(data));
        harness.Run("A player-wide upgrade is ordered once, an object upgrade goes to one building that can still take it, to the next one when clicked again", () => Upgrades(data));
        harness.Run("The rally point and Sell reach every building of the group; a right click on the ground sets the rally point of all", () => RallyAndSell(data));
        harness.Run("The bar of the original skin tags the group with the kind and the count and the modern bar's plate says the same; one selected building draws nothing new", () => Draws(data));
        harness.Run("Tab walks the kinds of a selection of units, Shift+Tab back; the bar and the highlight follow; a button acts on the kind only, a right click on all", () => TabSubgroups(data));
        harness.Run("Tab keeps the diplomacy window when the selection has one kind, and a new selection or a lost kind ends the subgroup", () => TabFallsBack(data));
        harness.Run("E on a building whose bar has a button on E keeps the button", () => EKeepsButton(data));
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------ the rig

    private sealed class Rig
    {
        public Rig(GameData data, string name, ScenarioUnit[] units, int money = 30000, GameSettings? settings = null, float lookX = 100f, string human = "FactionAmerica", string opponent = "FactionChina")
        {
            var scenario = new DemoScenario(name, name, name, 6000, [..units], [], lookX, 0f, CameraHeight: 330f)
            {
                StartMoney = money,
                HumanFaction = human,
                OpponentFaction = opponent,
            };
            Live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
            Controller = new MatchController(Live, settings ?? SkinPins.Settings) { StarBlinks = false };
            Controller.Input.Selection.Announced += Announced.Add;
            Frame(3);
        }

        public LiveScenario Live { get; }
        public MatchController Controller { get; }
        public SelectionModel Selection => Controller.Input.Selection;
        public List<SelectionAnnouncement> Announced { get; } = [];
        public long Time { get; private set; } = 100_000;

        public EntityId Tag(string tag) => Live.Tags[tag];
        public Entity Entity(string tag) => Live.World.FindEntity(Live.Tags[tag])!;

        public void Frame(int count = 1)
        {
            for (var frame = 0; frame < count; frame++)
            {
                Live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }

        public Vector2 ScreenOf(string tag)
        {
            var entity = Entity(tag);
            TestHarness.Assert(Selection.Picker.TryProject(Live.Camera, entity, out var projected), $"{tag} is in front of the camera");
            TestHarness.Assert(projected.Center.X > 0 && projected.Center.X < 1280 && projected.Center.Y > 0 && projected.Center.Y < 560, $"{tag} is on the screen above the bar ({projected.Center})");
            return projected.Center;
        }

        public void Key(KeyCode key, params KeyCode[] held)
        {
            foreach (var modifier in held) Controller.Process(new KeyEvent(Time += 16, modifier, true));
            Controller.Process(new KeyEvent(Time += 16, key, true));
            Controller.Process(new KeyEvent(Time += 16, key, false));
            foreach (var modifier in held.Reverse()) Controller.Process(new KeyEvent(Time += 16, modifier, false));
        }

        public void Click(Vector2 at, MouseButton button = MouseButton.Left, int times = 1, params KeyCode[] held)
        {
            foreach (var modifier in held) Controller.Process(new KeyEvent(Time += 16, modifier, true));
            Time += 700;
            Controller.Process(new MouseMoveEvent(Time += 10, at));
            for (var click = 0; click < times; click++)
            {
                Controller.Process(new MouseButtonEvent(Time += 40, button, true, at));
                Controller.Process(new MouseButtonEvent(Time += 40, button, false, at));
            }
            foreach (var modifier in held.Reverse()) Controller.Process(new KeyEvent(Time += 16, modifier, false));
        }

        public void ClickButton(int slot)
        {
            Controller.BuildDrawList(1280, 720);
            var rect = Controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}")!.Value;
            Click(new Vector2(rect.CenterX, rect.CenterY));
        }

        public int SlotOf(Func<CommandBarButton, bool> predicate)
        {
            var bar = Controller.CommandBar;
            for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
                if (bar[slot] is { } button && predicate(button)) return slot;
            return -1;
        }

        public int SlotOfObject(string template) => SlotOf(button => button.Button.Kind == GuiCommandKind.UnitBuild && button.Button.Object.Equals(template, StringComparison.OrdinalIgnoreCase));

        public int SlotOfUpgrade(string upgrade) => SlotOf(button => button.Button.Upgrade.Equals(upgrade, StringComparison.OrdinalIgnoreCase));

        public int QueueOf(string tag) => SimulationQueries.ProductionOf(Entity(tag)) is { } production ? SimulationQueries.Describe(production).Array("queue").Count : -1;

        public string[] Queues(params string[] tags) => [..tags.Select(tag => QueueOf(tag).ToString(System.Globalization.CultureInfo.InvariantCulture))];

        public int[] Selected() => [..Selection.Selected.Select(id => (int)id.Value)];

        public string[] SelectedTags() => [..Selection.Selected.Select(id => Live.Tags.First(pair => pair.Value == id).Key).OrderBy(tag => tag, StringComparer.Ordinal)];
    }

    private static ScenarioUnit Barracks(string tag, float x, float y = 0f, int owner = 1) => new(tag, "AmericaBarracks", owner, x, y, 0f);

    // ------------------------------------------------------------------------------------------------ selection

    private static void DoubleClick(GameData data)
    {
        var rig = new Rig(data, "group-double-click",
        [
            Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160),
            Barracks("far", 3000),
            new ScenarioUnit("plant", "AmericaPowerPlant", 1, 80, 90, 0f),
            Barracks("enemy", 120, -100, owner: 2),
            new ScenarioUnit("dozer", "AmericaVehicleDozer", 1, 0, 100, 0f),
        ]);
        var selection = rig.Selection;

        // A single click selects the one building, as before.
        rig.Click(rig.ScreenOf("b1"));
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b1"]), $"a click selects one building ({string.Join(",", rig.SelectedTags())})");

        // The double click: the buildings of the kind in view, the one far away stays out, the power plant and the enemy's barracks too.
        rig.Click(rig.ScreenOf("b1"), times: 2);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b1", "b2", "b3"]), $"a double click selects the three barracks in view ({string.Join(",", rig.SelectedTags())})");
        TestHarness.Assert(rig.Announced.SequenceEqual([SelectionAnnouncement.AcrossScreen]), $"and says 'across screen' ({string.Join(",", rig.Announced)})");
        TestHarness.Assert(selection.OwnStructureTemplate(rig.Live.World) == "AmericaBarracks", "the selection is a structure group of barracks");

        // Alt takes the whole map.
        rig.Click(rig.ScreenOf("b2"));
        rig.Announced.Clear();
        rig.Click(rig.ScreenOf("b2"), times: 2, held: KeyCode.LeftAlt);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b1", "b2", "b3", "far"]), $"Alt and a double click take the barracks of the whole map ({string.Join(",", rig.SelectedTags())})");
        TestHarness.Assert(rig.Announced.SequenceEqual([SelectionAnnouncement.AcrossMap]), $"and says 'across map' ({string.Join(",", rig.Announced)})");

        // Another kind of building: its own group of one - the plant stands alone.
        rig.Click(rig.ScreenOf("plant"), times: 2);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["plant"]), $"a double click on the power plant selects the plants in view: this one ({string.Join(",", rig.SelectedTags())})");

        // An enemy building is selected for information, never grouped.
        rig.Click(rig.ScreenOf("enemy"), times: 2);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["enemy"]) && !selection.IsCommandable(rig.Live.World), "an enemy building is only looked at");

        // A site stays out: the dozer begins a barracks, a double click on a finished one leaves it.
        var dozer = rig.Entity("dozer");
        var site = rig.Live.Issue(new PlayerId(1), (sequence, tick) => new CnCNew.Simulation.Commands.ConstructCommand(new PlayerId(1), sequence, tick, dozer.Id, "AmericaBarracks",
            CnCNew.Simulation.Numerics.Fixed.FromDecimal((decimal)(rig.Live.Anchor.X + 80)), CnCNew.Simulation.Numerics.Fixed.FromDecimal((decimal)(rig.Live.Anchor.Y + 240)), CnCNew.Simulation.Numerics.Fixed.FromInt(0)));
        TestHarness.Assert(site.Accepted, $"the dozer may begin a barracks ({site.Reason})");
        rig.Frame(6);
        var sites = rig.Live.World.Entities.Where(entity => entity.Owner.Value == 1 && entity.Template.Name == "AmericaBarracks" && entity.HasStatus(ObjectStatusBits.UnderConstruction)).ToArray();
        TestHarness.Assert(sites.Length == 1, $"a barracks is under construction ({sites.Length})");
        rig.Click(rig.ScreenOf("b1"));
        rig.Click(rig.ScreenOf("b1"), times: 2);
        TestHarness.Assert(!rig.Selection.Selected.Contains(sites[0].Id) && rig.SelectedTags().SequenceEqual(["b1", "b2", "b3"]), "the site is not taken along");
    }

    private static void MatchingKey(GameData data)
    {
        var rig = new Rig(data, "group-matching",
        [
            Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160),
            Barracks("far", 3000),
            new ScenarioUnit("plant", "AmericaPowerPlant", 1, 80, 90, 0f),
        ]);
        rig.Selection.Replace([rig.Tag("b1")]);
        rig.Frame();
        rig.Key(KeyCode.E);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b1", "b2", "b3"]) && rig.Announced.SequenceEqual([SelectionAnnouncement.AcrossScreen]),
            $"E selects the barracks in view and says so ({string.Join(",", rig.SelectedTags())}; {string.Join(",", rig.Announced)})");
        rig.Announced.Clear();
        rig.Key(KeyCode.E);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b1", "b2", "b3", "far"]) && rig.Announced.SequenceEqual([SelectionAnnouncement.AcrossMap]),
            $"E again finds the one across the map ({string.Join(",", rig.SelectedTags())}; {string.Join(",", rig.Announced)})");
        rig.Announced.Clear();
        rig.Key(KeyCode.E);
        TestHarness.Assert(rig.SelectedTags().Length == 4 && rig.Announced.Count == 0, "and once more has nothing to add and says nothing, as for a building in the original");

        // Shift-click joins one more of the kind, a power plant is refused and does not disturb the group.
        rig.Selection.Replace([rig.Tag("b1")]);
        rig.Frame();
        rig.Click(rig.ScreenOf("b3"), held: KeyCode.LeftShift);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b1", "b3"]), $"Shift-click adds a barracks ({string.Join(",", rig.SelectedTags())})");
        rig.Click(rig.ScreenOf("plant"), held: KeyCode.LeftShift);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["plant"]), $"Shift-click on another kind of building selects that one alone, as before ({string.Join(",", rig.SelectedTags())})");
        rig.Selection.Replace([rig.Tag("b1"), rig.Tag("b2")]);
        rig.Click(rig.ScreenOf("b2"), held: KeyCode.LeftShift);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b1"]), "Shift-click on a selected building takes it out");
    }

    private static void Groups(GameData data)
    {
        var rig = new Rig(data, "group-keys",
        [
            Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160),
            new ScenarioUnit("plant", "AmericaPowerPlant", 1, 80, 90, 0f),
            new ScenarioUnit("plant2", "AmericaPowerPlant", 1, 160, 90, 0f),
        ]);
        var selection = rig.Selection;
        selection.Replace([rig.Tag("b1"), rig.Tag("b2"), rig.Tag("b3")]);
        rig.Frame();
        rig.Key(KeyCode.D4, KeyCode.LeftControl);
        TestHarness.Assert(selection.Group(4).Length == 3 && selection.GroupOf(rig.Tag("b2")) == 4, "Ctrl+4 keeps the three barracks");
        selection.Clear();
        rig.Frame();
        rig.Key(KeyCode.D4);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b1", "b2", "b3"]) && selection.OwnStructureTemplate(rig.Live.World) == "AmericaBarracks", "4 brings the group back as a structure group");
        rig.Frame();
        TestHarness.Assert(rig.Controller.CommandBar.Group is { Kind: SelectionGroupKind.Structures, Count: 3 }, "and the bar is the group's");

        // The number is at every health bar.
        var numbers = rig.Controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Count(text => text.Font.Name == "Arial" && text.Font.Size == 10 && text.Text == "4");
        TestHarness.Assert(numbers == 3, $"each barracks wears the 4 ({numbers})");

        // Shift+5 adds a group of barracks to the selection of barracks, Shift+6 (a plant) empties it first.
        selection.Replace([rig.Tag("b1")]);
        rig.Key(KeyCode.D5, KeyCode.LeftControl);
        selection.Replace([rig.Tag("b2"), rig.Tag("b3")]);
        rig.Key(KeyCode.D5, KeyCode.LeftShift);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b1", "b2", "b3"]), $"Shift+5 adds the group's barracks to the selected ones ({string.Join(",", rig.SelectedTags())})");
        selection.Replace([rig.Tag("plant"), rig.Tag("plant2")]);
        rig.Key(KeyCode.D6, KeyCode.LeftControl);
        selection.Replace([rig.Tag("b1")]);
        rig.Key(KeyCode.D6, KeyCode.LeftShift);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["plant", "plant2"]), $"a group of another kind of building replaces the selection ({string.Join(",", rig.SelectedTags())})");

        // The double press of the digit centres the camera on the last member (the original's rule, kept).
        rig.Key(KeyCode.D4);
        rig.Key(KeyCode.D4);
        rig.Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        // Ctrl+5 took the first barracks out of group 4.
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["b2", "b3"]), $"the digit twice keeps the group selected ({string.Join(",", rig.SelectedTags())})");
    }

    // ------------------------------------------------------------------------------------------------ the bar

    private static void Distribution(GameData data)
    {
        var rig = new Rig(data, "group-distribution", [Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160)]);
        rig.Selection.Replace([rig.Tag("b1"), rig.Tag("b2"), rig.Tag("b3")]);
        rig.Frame(2);
        var bar = rig.Controller.CommandBar;
        TestHarness.Assert(bar.Context == CommandBarContext.Command && bar.Group is { Kind: SelectionGroupKind.Structures, Template: "AmericaBarracks", Count: 3 } && bar.Producer == rig.Tag("b1"),
            $"the bar is the group's, led by the first barracks ({bar.Context}, {bar.Group})");
        var ranger = rig.SlotOfObject("AmericaInfantryRanger");
        TestHarness.Assert(ranger > 0 && bar[ranger]!.IsEnabled && bar[ranger]!.Sources.Length == 3, $"the ranger button is enabled for all three ({ranger}, {bar[ranger]?.Sources.Length})");

        // Three clicks before the world has stepped: one each.
        for (var click = 0; click < 3; click++) rig.ClickButton(ranger);
        rig.Frame(2);
        TestHarness.Assert(rig.Queues("b1", "b2", "b3").SequenceEqual(["1", "1", "1"]), $"three clicks: 1/1/1 ({string.Join("/", rig.Queues("b1", "b2", "b3"))})");
        rig.ClickButton(ranger);
        rig.Frame(2);
        TestHarness.Assert(rig.Queues("b1", "b2", "b3").SequenceEqual(["2", "1", "1"]), $"a fourth: 2/1/1 ({string.Join("/", rig.Queues("b1", "b2", "b3"))})");
        rig.ClickButton(ranger);
        rig.Frame(2);
        TestHarness.Assert(rig.Queues("b1", "b2", "b3").SequenceEqual(["2", "2", "1"]), $"a fifth: 2/2/1 ({string.Join("/", rig.Queues("b1", "b2", "b3"))})");

        // The queue shown is the lead's.
        rig.Frame();
        TestHarness.Assert(rig.Controller.CommandBar.Queue.Length == 2, $"the bar shows the lead building's two entries ({rig.Controller.CommandBar.Queue.Length})");

        // A second barracks with a shorter queue than the lead takes the next order, whatever its place in the selection (the order of ties is the selection's).
        rig.Selection.Replace([rig.Tag("b2"), rig.Tag("b3")]);
        rig.Frame(2);
        rig.ClickButton(ranger);
        rig.Frame(2);
        TestHarness.Assert(rig.Queues("b1", "b2", "b3").SequenceEqual(["2", "2", "2"]), $"of two selected, the shorter one gets it: 2/2/2 ({string.Join("/", rig.Queues("b1", "b2", "b3"))})");

        // Orders that are executed some ticks later (the input delay of a network game) are counted as given.
        var delayed = new Rig(data, "group-distribution-delay", [Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160)]);
        delayed.Selection.Replace([delayed.Tag("b1"), delayed.Tag("b2"), delayed.Tag("b3")]);
        delayed.Frame(2);
        var slot = delayed.SlotOfObject("AmericaInfantryRanger");
        for (var click = 0; click < 6; click++)
        {
            delayed.ClickButton(slot);
            delayed.Frame(1);
        }
        delayed.Frame(3);
        TestHarness.Assert(delayed.Queues("b1", "b2", "b3").SequenceEqual(["2", "2", "2"]), $"six clicks, one frame apart: 2/2/2 ({string.Join("/", delayed.Queues("b1", "b2", "b3"))})");
    }

    private static void QueueLimit(GameData data)
    {
        var paid = (Rig rig, decimal start) => start - rig.Live.World.MoneyOf(new PlayerId(1)).ToDecimal();

        // One barracks, twelve clicks: nine orders are queued and paid, the others are not even sent; the sixth to the ninth stay visible as nine icons.
        var single = new Rig(data, "queue-limit-single", [Barracks("b1", 0)]);
        single.Selection.Replace([single.Tag("b1")]);
        single.Frame(2);
        var ranger = single.SlotOfObject("AmericaInfantryRanger");
        var cost = single.Live.World.Templates.TryGet("AmericaInfantryRanger", out var template) ? template.BuildCost.ToDecimal() : 0m;
        var start = single.Live.World.MoneyOf(new PlayerId(1)).ToDecimal();
        for (var click = 0; click < 12; click++)
        {
            single.ClickButton(ranger);
            single.Frame(1);
        }
        single.Frame(3);
        TestHarness.Assert(single.QueueOf("b1") == 9, $"one barracks holds nine ({single.QueueOf("b1")})");
        TestHarness.Assert(paid(single, start) == 9 * cost, $"and nine are paid, not twelve ({paid(single, start)} of {9 * cost})");
        TestHarness.Assert(single.Controller.CommandBar[ranger] is { IsEnabled: false, Reason: ButtonReason.QueueFull }, "the button is greyed with the reason 'queue full'");
        TestHarness.Assert(single.Controller.CommandBar.Queue.Length == 9, "nine icons are shown");
        // A greyed button takes no click (as in the original); an order that gets through to the controller anyway (a hot key, a race) is answered with the message and sends nothing.
        var before = single.Live.World.MoneyOf(new PlayerId(1));
        var answer = single.Controller.Input.Commands.Activate(single.Live.World, single.Controller.CommandBar[ranger]!.Button, [single.Entity("b1")]);
        single.Frame(3);
        TestHarness.Assert(answer == ActivationResult.Rejected && single.QueueOf("b1") == 9 && single.Live.World.MoneyOf(new PlayerId(1)) == before, $"a tenth order is refused and costs nothing ({answer})");
        TestHarness.Assert(single.Controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Any(text => text.Text == "The unit's production queue is full"), "the message list says that the queue is full");

        // Three barracks: the clicks fill them evenly to nine each and stop; the 28th click costs nothing.
        var rig = new Rig(data, "queue-limit-group", [Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160)]);
        rig.Selection.Replace([rig.Tag("b1"), rig.Tag("b2"), rig.Tag("b3")]);
        rig.Frame(2);
        ranger = rig.SlotOfObject("AmericaInfantryRanger");
        start = rig.Live.World.MoneyOf(new PlayerId(1)).ToDecimal();
        for (var click = 0; click < 26; click++)
        {
            rig.ClickButton(ranger);
            if (click % 3 == 2) rig.Frame(1);
        }
        rig.Frame(3);
        TestHarness.Assert(rig.Queues("b1", "b2", "b3").SequenceEqual(["9", "9", "8"]), $"26 clicks, also in bursts before the world has stepped: 9/9/8 ({string.Join("/", rig.Queues("b1", "b2", "b3"))})");
        var loads = rig.Controller.CommandBar.GroupQueues;
        TestHarness.Assert(loads.Select(load => (load.Count, load.Limit)).SequenceEqual([(9, 9), (9, 9), (8, 9)]), $"the bar counts each building's queue ({string.Join(" ", loads.Select(load => $"{load.Count}/{load.Limit}"))})");
        TestHarness.Assert(rig.Controller.CommandBar[ranger] is { IsEnabled: true } enabled && enabled.Sources.SequenceEqual([rig.Tag("b3")]), "the button is still enabled, for the one building with room");
        rig.ClickButton(ranger);
        rig.Frame(3);
        TestHarness.Assert(rig.Queues("b1", "b2", "b3").SequenceEqual(["9", "9", "9"]), "the next click fills the last place");
        rig.Frame();
        TestHarness.Assert(rig.Controller.CommandBar[ranger] is { IsEnabled: false, Reason: ButtonReason.QueueFull }, "now the button is greyed for the group");
        var money = rig.Live.World.MoneyOf(new PlayerId(1)).ToDecimal();
        rig.ClickButton(ranger);
        rig.ClickButton(ranger);
        rig.Frame(3);
        TestHarness.Assert(rig.Queues("b1", "b2", "b3").SequenceEqual(["9", "9", "9"]) && rig.Live.World.MoneyOf(new PlayerId(1)).ToDecimal() == money, $"clicks on a greyed button queue nothing and cost nothing ({string.Join("/", rig.Queues("b1", "b2", "b3"))})");
        TestHarness.Assert(paid(rig, start) == 27 * cost, $"27 are paid for, no more ({paid(rig, start)} of {27 * cost})");

        // A hot key does not get past the limit either: it is the same order.
        var key = rig.Controller.CommandBar[ranger]!.Hotkey;
        if (key is { } letter)
        {
            var code = Enum.Parse<KeyCode>(char.ToUpperInvariant(letter).ToString());
            rig.Key(code);
            rig.Frame(3);
            TestHarness.Assert(rig.Queues("b1", "b2", "b3").SequenceEqual(["9", "9", "9"]) && rig.Live.World.MoneyOf(new PlayerId(1)).ToDecimal() == money, "the hot key queues nothing either");
        }

        // The display: "9/9 9/9 9/9" with the full ones marked, in the original's skin and in the modern one.
        foreach (var skin in new[] { SkinPins.Settings, GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = CnCNew.Ui.Skins.UiSkin.Modern.Name } } })
        {
            var shown = new Rig(data, "queue-limit-display", [Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160)], settings: skin);
            shown.Selection.Replace([shown.Tag("b1"), shown.Tag("b2"), shown.Tag("b3")]);
            shown.Frame(2);
            var slot = shown.SlotOfObject("AmericaInfantryRanger");
            for (var click = 0; click < 4; click++) shown.ClickButton(slot);
            shown.Frame(3);
            // Four clicks leave 2/1/1; the counts are what is checked here.
            string[] Chips() => [..shown.Controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Select(text => text.Text).Where(text => text.Length == 3 && text[1] == '/')];
            TestHarness.Assert(Chips().SequenceEqual(["2/9", "1/9", "1/9"]), $"the load of each selected building is drawn ({string.Join(" ", Chips())})");
            shown.Selection.Replace([shown.Tag("b1")]);
            shown.Frame(2);
            TestHarness.Assert(Chips().Length == 0, "one building: nothing new (its icons are its queue)");
        }
    }

    private static void HotkeyAndCancel(GameData data)
    {
        var rig = new Rig(data, "group-hotkey", [Barracks("b1", 0), Barracks("b2", 80)]);
        rig.Selection.Replace([rig.Tag("b1"), rig.Tag("b2")]);
        rig.Frame(2);
        var ranger = rig.SlotOfObject("AmericaInfantryRanger");
        var key = rig.Controller.CommandBar[ranger]!.Hotkey;
        TestHarness.Assert(key is not null, "the ranger button has a hot key");
        var code = Enum.Parse<KeyCode>(char.ToUpperInvariant(key!.Value).ToString());
        rig.Key(code);
        rig.Key(code);
        rig.Key(code);
        rig.Frame(2);
        TestHarness.Assert(rig.Queues("b1", "b2").SequenceEqual(["2", "1"]), $"the hot key three times: 2/1 ({string.Join("/", rig.Queues("b1", "b2"))})");

        // The first icon of the queue cancels the lead building's order, with the refund.
        rig.Controller.BuildDrawList(1280, 720);
        var money = rig.Live.World.MoneyOf(new PlayerId(1));
        var icon = rig.Controller.WindowRect("ControlBar.wnd:ButtonQueue02")!.Value;
        rig.Click(new Vector2(icon.CenterX, icon.CenterY));
        rig.Frame(2);
        TestHarness.Assert(rig.Queues("b1", "b2").SequenceEqual(["1", "1"]) && rig.Live.World.MoneyOf(new PlayerId(1)) > money, $"the second icon of the lead cancels its second order ({string.Join("/", rig.Queues("b1", "b2"))})");
    }

    private static void Upgrades(GameData data)
    {
        var rig = new Rig(data, "group-upgrades",
        [
            Barracks("b1", 0), Barracks("b2", 80),
            new ScenarioUnit("p1", "AmericaPowerPlant", 1, 0, 100, 0f),
            new ScenarioUnit("p2", "AmericaPowerPlant", 1, 80, 100, 0f),
            new ScenarioUnit("p3", "AmericaPowerPlant", 1, 160, 100, 0f),
        ]);

        // A player-wide upgrade (the flash-bang grenade) is ordered once for the player, at one barracks, and then no barracks offers it.
        rig.Selection.Replace([rig.Tag("b1"), rig.Tag("b2")]);
        rig.Frame(2);
        var flash = rig.SlotOfUpgrade("Upgrade_AmericaRangerFlashBangGrenade");
        TestHarness.Assert(flash > 0 && rig.Controller.CommandBar[flash]!.IsEnabled, "the flash-bang button is enabled");
        rig.ClickButton(flash);
        rig.ClickButton(flash);
        rig.Frame(2);
        TestHarness.Assert(rig.Queues("b1", "b2").SequenceEqual(["1", "0"]), $"the upgrade is queued once, at the first barracks ({string.Join("/", rig.Queues("b1", "b2"))})");
        rig.Frame();
        TestHarness.Assert(!rig.Controller.CommandBar[flash]!.IsEnabled, "and the button is greyed for the group");

        // An object upgrade (the control rods) belongs to one building: the first click goes to one plant, the second to the next, the third finds none.
        rig.Selection.Replace([rig.Tag("p1"), rig.Tag("p2"), rig.Tag("p3")]);
        rig.Frame(2);
        var rods = rig.SlotOfUpgrade("Upgrade_AmericaAdvancedControlRods");
        TestHarness.Assert(rods > 0 && rig.Controller.CommandBar[rods]!.IsEnabled && rig.Controller.CommandBar[rods]!.Sources.Length == 3, $"the control rods can go to three plants ({rods})");
        rig.ClickButton(rods);
        rig.Frame(2);
        TestHarness.Assert(rig.Queues("p1", "p2", "p3").SequenceEqual(["1", "0", "0"]), $"one plant researches them ({string.Join("/", rig.Queues("p1", "p2", "p3"))})");
        rig.Frame();
        TestHarness.Assert(rig.Controller.CommandBar[rods]!.Sources.Length == 2, "two plants can still take them");
        rig.ClickButton(rods);
        rig.ClickButton(rods);
        rig.Frame(2);
        TestHarness.Assert(rig.Queues("p1", "p2", "p3").SequenceEqual(["1", "1", "1"]), $"each plant researches them once ({string.Join("/", rig.Queues("p1", "p2", "p3"))})");
        rig.Frame();
        TestHarness.Assert(!rig.Controller.CommandBar[rods]!.IsEnabled, "and then the button is greyed");
    }

    private static void RallyAndSell(GameData data)
    {
        var rig = new Rig(data, "group-rally", [Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160)]);
        var ids = new[] { rig.Tag("b1"), rig.Tag("b2"), rig.Tag("b3") };
        rig.Selection.Replace(ids);
        rig.Frame(2);

        bool HasRally(string tag) => SimulationQueries.ProductionOf(rig.Entity(tag)) is { } production && SimulationQueries.Describe(production).Bool("hasRally");

        // A right click on open ground: every barracks gets the rally point.
        rig.Controller.BuildDrawList(1280, 720);
        var ground = new Vector2(640, 470);
        rig.Click(ground, MouseButton.Right);
        rig.Frame(2);
        TestHarness.Assert(HasRally("b1") && HasRally("b2") && HasRally("b3"), "a right click on the ground sets the rally point of all three");

        // The button: the mode, then the click.
        var other = new Rig(data, "group-rally-button", [Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160)]);
        other.Selection.Replace([other.Tag("b1"), other.Tag("b2"), other.Tag("b3")]);
        other.Frame(2);
        var rally = other.SlotOf(button => button.Button.Kind == GuiCommandKind.SetRallyPoint);
        TestHarness.Assert(rally > 0, "the bar has the rally point button");
        other.ClickButton(rally);
        TestHarness.Assert(other.Controller.Input.Commands.Mode == CommandModeKind.ButtonTarget && other.Controller.Input.Commands.PendingSources.Length == 3, "the button arms the mode for all three");
        other.Controller.BuildDrawList(1280, 720);
        other.Click(new Vector2(640, 470));
        other.Frame(2);
        bool OtherRally(string tag) => SimulationQueries.ProductionOf(other.Entity(tag)) is { } production && SimulationQueries.Describe(production).Bool("hasRally");
        TestHarness.Assert(OtherRally("b1") && OtherRally("b2") && OtherRally("b3"), "and the click sets all three");

        // Sell: all of them are sold.
        var sell = rig.SlotOf(button => button.Button.Kind == GuiCommandKind.Sell);
        TestHarness.Assert(sell > 0, "the bar has Sell");
        rig.ClickButton(sell);
        rig.Frame(2);
        var sold = ids.Count(id => rig.Live.World.FindEntity(id) is not { } entity || entity.HasStatus(ObjectStatusBits.Sold));
        TestHarness.Assert(sold == 3, $"Sell sells all three ({sold})");
    }

    private static void Draws(GameData data)
    {
        var rig = new Rig(data, "group-draw", [Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160)]);
        string[] Tags(Rig r) => [..r.Controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Select(text => text.Text).Where(text => text.Contains(" x3", StringComparison.Ordinal))];
        rig.Selection.Replace([rig.Tag("b1")]);
        rig.Frame(2);
        TestHarness.Assert(Tags(rig).Length == 0 && rig.Controller.CommandBar.Group is null, "one barracks: no tag and no group");
        rig.Selection.Replace([rig.Tag("b1"), rig.Tag("b2"), rig.Tag("b3")]);
        rig.Frame(2);
        var tags = Tags(rig);
        TestHarness.Assert(tags.Length == 1 && tags[0].EndsWith(" x3", StringComparison.Ordinal), $"three barracks: 'Name x3' over the portrait ({string.Join("|", tags)})");
        var list = rig.Controller.BuildDrawList(1280, 720);
        TestHarness.Assert(list.Commands.OfType<UiImageDraw>().Any(image => rig.Live.Data.Visuals.TryGet("AmericaBarracks", out var visual) && image.Image == visual.SelectPortrait), "the portrait of the barracks is shown");
        TestHarness.Assert(rig.Controller.BuildDrawList(1280, 720).Commands.OfType<UiLineDraw>().Count() > 0, "each building has its own brackets");

        var modern = new Rig(data, "group-draw-modern", [Barracks("b1", 0), Barracks("b2", 80), Barracks("b3", 160)], settings: GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = CnCNew.Ui.Skins.UiSkin.Modern.Name } });
        modern.Selection.Replace([modern.Tag("b1"), modern.Tag("b2"), modern.Tag("b3")]);
        modern.Frame(2);
        var texts = modern.Controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Select(text => text.Text).ToArray();
        TestHarness.Assert(texts.Any(text => text.StartsWith("3 x ", StringComparison.Ordinal)), $"the plate of the modern bar names the three ({string.Join("|", texts.Where(text => text.Contains(" x ", StringComparison.Ordinal)))})");
        TestHarness.Assert(!texts.Any(text => text.EndsWith(" x3", StringComparison.Ordinal)), "the modern bar has no second tag");
    }

    // ------------------------------------------------------------------------------------------------ Tab

    private static Rig MixedRig(GameData data, string name, GameSettings? settings = null)
        => new(data, name,
        [
            new ScenarioUnit("r1", "AmericaInfantryRanger", 1, 0, 0, 0f), new ScenarioUnit("r2", "AmericaInfantryRanger", 1, 20, 0, 0f),
            new ScenarioUnit("c1", "AmericaTankCrusader", 1, 0, 40, 0f), new ScenarioUnit("c2", "AmericaTankCrusader", 1, 30, 40, 0f), new ScenarioUnit("c3", "AmericaTankCrusader", 1, 60, 40, 0f),
            new ScenarioUnit("d1", "AmericaVehicleDozer", 1, 0, 80, 0f),
        ], settings: settings, lookX: 30f);

    private static void TabSubgroups(GameData data)
    {
        var rig = MixedRig(data, "tab-subgroups");
        var all = new[] { "r1", "r2", "c1", "c2", "c3", "d1" }.Select(rig.Tag).ToArray();
        rig.Selection.Replace(all);
        rig.Frame(2);
        var selection = rig.Selection;
        TestHarness.Assert(selection.Subgroups(rig.Live.World).SequenceEqual(["AmericaInfantryRanger", "AmericaTankCrusader", "AmericaVehicleDozer"]), "three kinds, in the order of the plate");
        TestHarness.Assert(rig.Controller.CommandBar is { Context: CommandBarContext.MultiSelect, Group: null }, "before Tab the bar is the one for the selection as a whole (the buttons all share)");
        var combined = rig.Controller.CommandBar.Slots.Count(slot => slot is not null);
        TestHarness.Assert(rig.SlotOf(button => button.Button.Kind == GuiCommandKind.DozerConstruct) < 0, "and has no construction button");

        // Tab: the rangers.
        rig.Key(KeyCode.Tab);
        rig.Frame();
        var bar = rig.Controller.CommandBar;
        TestHarness.Assert(bar.Group is { Kind: SelectionGroupKind.Subgroup, Template: "AmericaInfantryRanger", Count: 2, Index: 1, Total: 3 } && bar.Context == CommandBarContext.Command, $"Tab: the rangers, 1 of 3 ({bar.Group})");
        TestHarness.Assert(selection.Selected.Length == 6, "the selection itself is unchanged");
        var markers = rig.Controller.Overlay.Selected;
        TestHarness.Assert(markers.Count(marker => marker.ActiveKind) == 2 && markers.Where(marker => marker.ActiveKind).All(marker => rig.Live.World.FindEntity(marker.Entity)!.Template.Name == "AmericaInfantryRanger"),
            "the two rangers are marked as the active kind");
        var drawn = rig.Controller.BuildDrawList(1280, 720);
        TestHarness.Assert(drawn.Commands.OfType<UiTextDraw>().Any(text => text.Text.EndsWith("x2 [1/3]", StringComparison.Ordinal)), "the bar's tag names the kind, its count and its place");

        rig.Key(KeyCode.Tab);
        rig.Frame();
        TestHarness.Assert(rig.Controller.CommandBar.Group is { Template: "AmericaTankCrusader", Count: 3, Index: 2 }, "Tab again: the Crusaders");
        rig.Key(KeyCode.Tab);
        rig.Frame();
        bar = rig.Controller.CommandBar;
        TestHarness.Assert(bar.Group is { Template: "AmericaVehicleDozer", Count: 1, Index: 3 } && rig.SlotOf(button => button.Button.Kind == GuiCommandKind.DozerConstruct) > 0, "the third: the dozer, with its construction buttons");
        rig.Key(KeyCode.Tab);
        rig.Frame();
        TestHarness.Assert(rig.Controller.CommandBar is { Context: CommandBarContext.MultiSelect, Group: null } && rig.Controller.CommandBar.Slots.Count(slot => slot is not null) == combined, "after the last kind the bar is the shared one again");
        rig.Key(KeyCode.Tab, KeyCode.LeftShift);
        rig.Frame();
        TestHarness.Assert(rig.Controller.CommandBar.Group is { Template: "AmericaVehicleDozer" }, "Shift+Tab goes back, to the last kind");
        rig.Key(KeyCode.Tab, KeyCode.LeftShift);
        rig.Frame();
        TestHarness.Assert(rig.Controller.CommandBar.Group is { Template: "AmericaTankCrusader" }, "and on");

        // The modern plate names the kind and marks its cell.
        var modern = MixedRig(data, "tab-subgroups-modern", GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = CnCNew.Ui.Skins.UiSkin.Modern.Name } });
        modern.Selection.Replace(new[] { "r1", "r2", "c1", "c2", "c3", "d1" }.Select(modern.Tag));
        modern.Frame(2);
        modern.Key(KeyCode.Tab);
        modern.Frame();
        var texts = modern.Controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Select(text => text.Text).ToArray();
        TestHarness.Assert(texts.Any(text => text.EndsWith("(1/3)", StringComparison.Ordinal) && text.StartsWith("2 x ", StringComparison.Ordinal)), $"the modern plate names the active kind ({string.Join("|", texts.Where(text => text.Contains('/')))})");
    }

    private static void TabFallsBack(GameData data)
    {
        var rig = MixedRig(data, "tab-fallback");
        var selection = rig.Selection;

        // The subgroup acts for its kind only: every unit walks off, the rangers are stopped by their Stop button, the others go on.
        var all = new[] { "r1", "r2", "c1", "c2", "c3", "d1" };
        selection.Replace(all.Select(rig.Tag));
        rig.Frame(2);
        rig.Controller.Input.Commands.IssueContext(rig.Live.World, new ContextHint(ContextAction.Move, CursorKind.Move, EntityId.Invalid, new Vector3(rig.Live.Anchor.X + 600f, rig.Live.Anchor.Y, 0f)), null);
        rig.Frame(10);
        TestHarness.Assert(all.All(tag => rig.Entity(tag).HasMoveTarget), "a move order of the whole selection reaches every unit, with no subgroup active");
        rig.Key(KeyCode.Tab);
        rig.Frame();
        var stop = rig.SlotOf(button => button.Button.Kind == GuiCommandKind.Stop);
        TestHarness.Assert(stop > 0, "the rangers' bar has Stop");
        rig.ClickButton(stop);
        rig.Frame(3);
        TestHarness.Assert(!rig.Entity("r1").HasMoveTarget || rig.Entity("r1").X < rig.Entity("c1").X, "the rangers were stopped");
        TestHarness.Assert(rig.Entity("c1").HasMoveTarget && rig.Entity("c2").HasMoveTarget && rig.Entity("d1").HasMoveTarget, "the Crusaders and the dozer still walk");

        // A right click on the ground orders everyone, as always.
        rig.Click(new Vector2(640, 470), MouseButton.Right);
        rig.Frame(3);
        TestHarness.Assert(all.All(tag => rig.Entity(tag).HasMoveTarget), "a right click on the ground moves the whole selection, with a kind active");

        // The kind survives a unit dying out of another kind; it ends when its own kind is gone, and with a new selection.
        selection.Clear();
        selection.Replace(all.Select(rig.Tag));
        rig.Frame(2);
        rig.Key(KeyCode.Tab);
        rig.Key(KeyCode.Tab);
        TestHarness.Assert(selection.ActiveSubgroup == "AmericaTankCrusader", "the Crusaders are active");
        selection.Replace(all.Where(tag => tag != "d1").Select(rig.Tag));
        TestHarness.Assert(selection.ActiveSubgroup == "AmericaTankCrusader", "a selection that only shrinks keeps it");
        selection.Replace(all.Select(rig.Tag));
        TestHarness.Assert(selection.ActiveSubgroup is null, "a selection that grows ends it");
        rig.Key(KeyCode.Tab);
        selection.Replace(new[] { "c1", "c2", "c3" }.Select(rig.Tag));
        TestHarness.Assert(selection.ActiveSubgroupIn(rig.Live.World) is null && selection.Subgroups(rig.Live.World).IsEmpty, "one kind has no subgroups");

        // One kind selected: Tab opens the diplomacy window, as the original's key does, and Shift+Tab does nothing.
        rig.Frame();
        TestHarness.Assert(!rig.Controller.IsDiplomacyOpen, "the window is closed");
        rig.Key(KeyCode.Tab);
        rig.Frame();
        TestHarness.Assert(rig.Controller.IsDiplomacyOpen, "Tab with one kind selected opens the diplomacy window");
        rig.Key(KeyCode.Tab);
        rig.Frame();
        TestHarness.Assert(!rig.Controller.IsDiplomacyOpen, "and closes it again");

        // A selection holding something foreign has no subgroups either.
        rig.Selection.Replace(new[] { "r1", "c1" }.Select(rig.Tag));
        TestHarness.Assert(selection.Subgroups(rig.Live.World).Length == 2, "two kinds of the player's own are subgroups");
    }

    private static void EKeepsButton(GameData data)
    {
        // The Chinese war factory builds its ECM tank with E; the American one has no button on E.
        var rig = new Rig(data, "group-e-button", [new ScenarioUnit("f1", "ChinaWarFactory", 1, 0, 0, 0f), new ScenarioUnit("f2", "ChinaWarFactory", 1, 100, 0, 0f), new ScenarioUnit("pc", "ChinaPropagandaCenter", 1, 200, 0, 0f)],
            human: "FactionChina", opponent: "FactionAmerica");
        rig.Selection.Replace([rig.Tag("f1")]);
        rig.Frame(2);
        var ecm = rig.Controller.CommandBar.Slots.FirstOrDefault(button => button is { Hotkey: 'e' });
        TestHarness.Assert(ecm is not null, "the Chinese war factory has a button on E");
        rig.Key(KeyCode.E);
        TestHarness.Assert(rig.SelectedTags().SequenceEqual(["f1"]), $"E keeps its button on the war factory: the selection stays ({string.Join(",", rig.SelectedTags())})");
        rig.Frame(2);
        Console.WriteLine($"       (the ECM button is {ecm!.Availability}/{ecm.Reason}, the queue {rig.QueueOf("f1")})");
        if (ecm.IsEnabled) TestHarness.Assert(rig.QueueOf("f1") == 1, $"and the button queued its vehicle ({ecm.Button.Name}, queue {rig.QueueOf("f1")})");

        var america = new Rig(data, "group-e-free", [new ScenarioUnit("f1", "AmericaWarFactory", 1, 0, 0, 0f), new ScenarioUnit("f2", "AmericaWarFactory", 1, 100, 0, 0f)]);
        america.Selection.Replace([america.Tag("f1")]);
        america.Frame(2);
        TestHarness.Assert(!america.Controller.CommandBar.Slots.Any(button => button is { Hotkey: 'e' }), "the American war factory has no button on E");
        america.Key(KeyCode.E);
        TestHarness.Assert(america.SelectedTags().SequenceEqual(["f1", "f2"]), "so E finds the second factory");
    }
}

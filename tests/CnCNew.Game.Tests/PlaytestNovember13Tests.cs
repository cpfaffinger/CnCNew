using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Simulation.Skirmish;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Skins;
using CnCNew.Ui.Wnd;
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
        harness.Run("A colour named twice stays with the first slot, every random slot gets the lowest free colour: no two players share one (issue 307)", ColorsResolve);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP PlaytestNovember13Tests on real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A selected own barracks with a rally point shows the line to it in the waypoint colour; an enemy's barracks and one without a rally point show none (issue 306)", () => RallyLineOnScreen(data));
        harness.Run("The setup's colour boxes start at ???, offer only the colours nobody holds and give a colour back when its slot opens (issue 307)", () => SetupColorBoxes(data));
        harness.Run("A skirmish draws every player in the multiplayer colour of its slot, and two players of one faction's colour or of ??? never look alike (issue 307)", () => MatchHouseColors(data));
        harness.Run("The colour box writes the chosen colour and every row of its list in that colour, in both skins (issue 308)", () => ColorBoxesShowTheirColor(data));
        harness.Run("W with nothing selected takes the aircraft on the screen and nothing else, as the original's SELECT_ALL_AIRCRAFT; Q takes every unit but the dozer (issue 313)", () => SelectAllKeys(data));
        harness.Run("A Fire Base shows the soldiers inside on its four places with their cameos and lets one out by a click (issue 317)", () => FireBaseRiders(data));
        harness.Run("The portable structures of the Avenger, the Overlord and the Helix are drawn on their hosts' bones, the soldier in an Overlord's bunker is not (issue 322)", () => PortableStructuresDrawn(data));
        harness.Run("The healing icon over a repaired building is the wrench at half its frame grown with the screen, the cross over a soldier likewise (issue 309)", () => HealIconSize(data));
        harness.Run("A tank leaves track marks of its draw module's texture while it drives, as wide as the distance of its tread bones, and none while it stands (issue 323)", () => TreadMarksOfATank(data));
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------ #309 the size of the healing icon

    // ------------------------------------------------------------------------------------------------ #323 track marks

    private static void TreadMarksOfATank(GameData data)
    {
        var scenario = new DemoScenario("tread-marks", "tread-marks", "tread-marks", 6000,
            [new ScenarioUnit("tank", "AmericaTankCrusader", 1, 0, -80, 0f)], [], 100f, 40f)
        {
            StartMoney = 50000,
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        void Frame() => live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        for (var frame = 0; frame < 10; frame++) Frame();
        TestHarness.Assert(live.Fx.Capture().TreadMarks.IsEmpty, "a tank that stands leaves nothing");
        var tank = live.Tags["tank"];
        var home = live.World.FindEntity(tank)!;
        var ordered = live.Issue(new PlayerId(1), (sequence, tick) => new GroupMoveCommand(new PlayerId(1), sequence, tick, [tank], home.X + Fixed.FromInt(300), home.Y, false));
        TestHarness.Assert(ordered.Accepted, $"the tank may drive ({ordered.Reason})");
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 12; frame++) Frame();
        var strips = live.Fx.Capture().TreadMarks;
        TestHarness.Assert(strips.Length == 1, $"one track of the tank ({strips.Length})");
        var strip = strips[0];
        TestHarness.Assert(strip.Texture.Equals("EXTnkTrack.tga", StringComparison.OrdinalIgnoreCase) || strip.Texture.Contains("Track", StringComparison.OrdinalIgnoreCase), $"the texture of its draw module ({strip.Texture})");
        TestHarness.Assert(strip.Edges.Length >= 5, $"the tank laid edges ({strip.Edges.Length})");
        var width = (strip.Edges[^1].Left - strip.Edges[^1].Right).Length();
        TestHarness.Assert(width is > 8f and < 60f, $"the track is as wide as the tank's treads ({width})");
        var spacing = (strip.Edges[^1].Left - strip.Edges[^2].Left).Length();
        TestHarness.Assert(spacing is >= 9.9f and < 25f, $"an edge for every ten units travelled ({spacing})");
        TestHarness.Assert(strip.Edges[^1].Alpha > 0.9f, $"the newest edge is opaque ({strip.Edges[^1].Alpha})");
    }

    private static void HealIconSize(GameData data)
    {
        foreach (var (width, height) in new[] { (800, 600), (1280, 720), (1920, 1080) })
        {
            var scenario = new DemoScenario("heal-icon-size", "heal-icon-size", "heal-icon-size", 3000,
                [new ScenarioUnit("center", "AmericaCommandCenter", 1, 0, 0, 0f), new ScenarioUnit("ranger", "AmericaInfantryRanger", 1, 120, 60, 0f)], [], 40f, 0f, CameraHeight: 330f)
            {
                HumanFaction = "FactionAmerica",
                OpponentFaction = "FactionChina",
            };
            var live = LiveScenario.Create(data, scenario, new Vector2(width, height));
            var controller = new MatchController(live, SkinPins.Settings) { StarBlinks = false };
            void Frames(int count)
            {
                for (var frame = 0; frame < count; frame++)
                {
                    live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                    controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
                }
            }
            Frames(4 * CnCNew.Tests.TestTime.FramesPerSecond); // Drawable::drawHealing ignores the first three seconds of a game
            var (center, ranger) = (live.Tags["center"], live.Tags["ranger"]);
            ulong sequence = 900_000;
            void Hit(EntityId target, int amount, int type)
                => live.World.Submit(new DamageCommand(PlayerId.System, ++sequence, live.World.Tick, target, Fixed.FromInt(amount), type, 0));
            Hit(center, 800, CnCNew.Simulation.World.DamageTypes.Unresistable);
            Hit(ranger, 60, CnCNew.Simulation.World.DamageTypes.Unresistable);
            Frames(3);
            Hit(center, 100, CnCNew.Simulation.World.DamageTypes.Healing);
            Hit(ranger, 10, CnCNew.Simulation.World.DamageTypes.Healing);
            Frames(3);
            var scale = Math.Min(width / 800f, height / 600f);
            var images = controller.BuildDrawList(width, height).Commands.OfType<UiImageDraw>().ToArray();
            var wrench = images.FirstOrDefault(image => image.Image.StartsWith("SCPWrench", StringComparison.Ordinal));
            var cross = images.FirstOrDefault(image => image.Image.StartsWith("SCPCross", StringComparison.Ordinal));
            TestHarness.Assert(wrench is not null && wrench.Destination.Width == (int)(28 * scale) && wrench.Destination.Height == (int)(28 * scale),
                $"{width}x{height}: the wrench over the building is half its frame, 28 pixels of an 800 x 600 screen ({wrench?.Destination})");
            TestHarness.Assert(cross is not null && cross.Destination.Width == (int)(32 * scale), $"{width}x{height}: the cross over the soldier is 32 pixels of an 800 x 600 screen ({cross?.Destination})");
        }
    }

    // ------------------------------------------------------------------------------------------------ #313 the W key

    private static void SelectAllKeys(GameData data)
    {
        var scenario = new DemoScenario("select-all-keys", "select-all-keys", "select-all-keys", 6000,
            [
                new ScenarioUnit("tank1", "AmericaTankCrusader", 1, 0, 0, 0f), new ScenarioUnit("tank2", "AmericaTankCrusader", 1, 40, 0, 0f),
                new ScenarioUnit("ranger", "AmericaInfantryRanger", 1, 0, 60, 0f), new ScenarioUnit("dozer", "AmericaVehicleDozer", 1, 80, 60, 0f),
                new ScenarioUnit("heli1", "AmericaVehicleComanche", 1, 60, -60, 0f), new ScenarioUnit("heli2", "AmericaVehicleComanche", 1, 100, -60, 0f),
            ], [], 40f, 0f, CameraHeight: 330f)
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
        var time = 100_000L;
        void Press(KeyCode key)
        {
            controller.Process(new KeyEvent(time += 16, key, true));
            controller.Process(new KeyEvent(time += 16, key, false));
            Frame();
        }
        string[] Selected() => [.. controller.Input.Selection.Selected.Select(id => live.Tags.First(pair => pair.Value == id).Key).Order(StringComparer.Ordinal)];
        Frame(30);
        controller.Input.Selection.Replace([]);
        Frame();

        Press(KeyCode.W);
        TestHarness.Assert(Selected().SequenceEqual(["heli1", "heli2"]), $"W takes the two Comanches only ({string.Join(",", Selected())})");

        controller.Input.Selection.Replace([]);
        Frame();
        Press(KeyCode.Q);
        TestHarness.Assert(Selected().SequenceEqual(["heli1", "heli2", "ranger", "tank1", "tank2"]), $"Q takes every unit on the screen but the dozer ({string.Join(",", Selected())})");
    }

    // ------------------------------------------------------------------------------------------------ #322 the portable structures

    private static void PortableStructuresDrawn(GameData data)
    {
        var scenario = new DemoScenario("portable-structures", "portable-structures", "portable-structures", 6000,
            [
                new ScenarioUnit("avenger", "AmericaTankAvenger", 1, 0, -80, 0f), new ScenarioUnit("overlord", "ChinaTankOverlord", 1, 0, 80, 0f),
                new ScenarioUnit("bunkered", "ChinaTankOverlord", 1, 160, 80, 0f), new ScenarioUnit("helix", "ChinaVehicleHelix", 1, 160, -80, 0f),
                new ScenarioUnit("soldier", "ChinaInfantryRedguard", 1, 230, 140, 0f),
            ], [], 100f, 40f)
        {
            StartMoney = 50000,
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        void Frame() => live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        for (var frame = 0; frame < 10; frame++) Frame();
        foreach (var (host, upgrade) in new[] { ("overlord", "Upgrade_ChinaOverlordGattlingCannon"), ("bunkered", "Upgrade_ChinaOverlordBattleBunker"), ("helix", "Upgrade_ChinaHelixGattlingCannon") })
        {
            var bought = live.Issue(new PlayerId(1), (sequence, tick) => new PurchaseUpgradeCommand(new PlayerId(1), sequence, tick, upgrade, live.Tags[host]));
            TestHarness.Assert(bought.Accepted, $"the {host} may buy {upgrade} ({bought.Reason})");
        }
        CnCNew.Simulation.World.Entity? Rider(string host, string template) =>
            live.World.Entities.FirstOrDefault(entity => entity.ContainedBy == live.Tags[host] && entity.Template.Name == template);
        var structures = new[] { ("avenger", "AmericaTankAvengerLaserTurret"), ("overlord", "ChinaTankOverlordGattlingCannon"), ("bunkered", "ChinaTankOverlordBattleBunker"), ("helix", "ChinaHelixGattlingCannon") };
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 90 && structures.Any(pair => Rider(pair.Item1, pair.Item2) is null); frame++) Frame();
        for (var frame = 0; frame < 3; frame++) Frame();
        CnCNew.Presentation.Objects.DrawableInstance? Drawn(EntityId id) => live.Frame.Drawables.FirstOrDefault(drawable => drawable.Entity == id);
        foreach (var (host, template) in structures)
        {
            var rider = Rider(host, template);
            TestHarness.Assert(rider is not null, $"the {host} carries its {template}");
            var (drawn, carrier) = (Drawn(rider!.Id), Drawn(live.Tags[host]));
            TestHarness.Assert(drawn is not null && carrier is not null, $"the {template} on the {host} is drawn with it (isEnclosingContainerFor: not its portable structure)");
            // W3DDependencyModelDraw::adjustTransformMtx: on the host's AttachToBoneInContainer bone - FIREPOINT01 of the Avenger and the Overlord lies on the turret, above the host's
            // place; the Helix's CHASSISBONE lies at its origin.
            if (host != "helix")
                TestHarness.Assert(drawn!.Position.Z > carrier!.Position.Z + 1f, $"the {template} stands on its host's bone, not at its place ({drawn.Position}, host {carrier.Position})");
            // #326: what starts from the drawable (a laser's start, the bones and the muzzle of an effect) starts where the structure is drawn.
            var pose = live.FxView.GetPose(rider.Id);
            TestHarness.Assert(pose is { } p && System.Numerics.Vector3.Distance(p.Position, drawn!.Position) < 0.01f, $"the effects' pose of the {template} is where it is drawn ({pose?.Position}, drawn {drawn!.Position})");
        }

        // A soldier in the Overlord's bunker is inside the bunker: drawn by neither.
        var soldier = live.Tags["soldier"];
        var entered = live.Issue(new PlayerId(1), (sequence, tick) => new MoveAndEnterCommand(new PlayerId(1), sequence, tick, soldier, live.Tags["bunkered"]));
        TestHarness.Assert(entered.Accepted, $"the soldier may enter the Overlord ({entered.Reason})");
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 30 && !live.World.FindEntity(soldier)!.ContainedBy.IsValid; frame++) Frame();
        for (var frame = 0; frame < 3; frame++) Frame();
        var bunker = Rider("bunkered", "ChinaTankOverlordBattleBunker")!;
        // The bunker lists the soldier; the port keeps the Overlord as the soldier's container (OverlordContain's redirection), which encloses everybody but its portable structure.
        TestHarness.Assert(bunker.Container is { } inBunker && inBunker.Contained.Contains(soldier), $"the soldier sits in the bunker ({live.World.FindEntity(live.World.FindEntity(soldier)!.ContainedBy)?.Template.Name})");
        TestHarness.Assert(Drawn(soldier) is null && Drawn(bunker.Id) is not null, "the bunker is drawn, the soldier in it is not");
    }

    // ------------------------------------------------------------------------------------------------ #317 the Fire Base's riders

    private static void FireBaseRiders(GameData data)
    {
        foreach (var skin in new[] { UiSkinName.Original, UiSkinName.Modern })
        {
            var scenario = new DemoScenario("fire-base-riders", "fire-base-riders", "fire-base-riders", 3000,
                [
                    new ScenarioUnit("dozer", "AmericaVehicleDozer", 1, 60, -80, 0f), new ScenarioUnit("barracks", "AmericaBarracks", 1, -200, 0, 0f),
                    new ScenarioUnit("plant", "AmericaPowerPlant", 1, -200, 150, 0f),
                    new ScenarioUnit("first", "AmericaInfantryRanger", 1, 40, 0, 0f), new ScenarioUnit("second", "AmericaInfantryMissileDefender", 1, 40, 40, 0f),
                ], [], 100f, 40f)
            {
                StartMoney = 20000,
                HumanFaction = "FactionAmerica",
                OpponentFaction = "FactionChina",
            };
            var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
            var controller = new MatchController(live, GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = skin } }) { StarBlinks = false };
            void Frame()
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
            var time = 40_000L;
            void Click(Vector2 at, MouseButton button)
            {
                controller.Process(new MouseMoveEvent(time += 700, at));
                controller.Process(new MouseButtonEvent(time += 40, button, true, at));
                controller.Process(new MouseButtonEvent(time += 40, button, false, at));
                Frame();
            }
            Vector2 ScreenOf(EntityId id)
            {
                TestHarness.Assert(controller.Input.Selection.Picker.TryProject(live.Camera, live.World.FindEntity(id)!, out var projected), "the object is in view");
                return projected.Center;
            }
            // The person's dozer builds the Fire Base.
            for (var frame = 0; frame < 10; frame++) Frame();
            var built = live.Issue(new PlayerId(1), (sequence, tick) => new ConstructCommand(new PlayerId(1), sequence, tick, live.Tags["dozer"], "AmericaFireBase",
                Fixed.FromDecimal((decimal)(live.Anchor.X + 150)), Fixed.FromDecimal((decimal)(live.Anchor.Y)), Fixed.Zero));
            TestHarness.Assert(built.Accepted, $"{skin}: the dozer may build a Fire Base ({built.Reason})");
            CnCNew.Simulation.World.Entity? Base() => live.World.Entities.FirstOrDefault(entity => entity.Owner.Value == 1 && entity.Template.Name == "AmericaFireBase");
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 90 && Base()?.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction) != false; frame++) Frame();
            TestHarness.Assert(Base() is { } finished && !finished.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction), $"{skin}: the Fire Base stands");
            var (fireBase, first, second) = (Base()!.Id, live.Tags["first"], live.Tags["second"]);
            bool Inside(EntityId id) => live.World.FindEntity(id)!.ContainedBy == fireBase;
            for (var frame = 0; frame < 10; frame++) Frame();

            // As a person does it: the soldiers selected, a right click on the Fire Base, then a click on the Fire Base.
            controller.Input.Selection.Replace([first, second]);
            Frame();
            Click(ScreenOf(fireBase), MouseButton.Right);
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 30 && !(Inside(first) && Inside(second)); frame++) Frame();
            TestHarness.Assert(Inside(first) && Inside(second), $"{skin}: both soldiers are inside the Fire Base");
            // The Fire Base encloses nobody (IsEnclosingContainer = No): its soldiers stand on its stations and are drawn there, their heads in its slots.
            for (var frame = 0; frame < 3; frame++) Frame();
            foreach (var rider in new[] { first, second })
            {
                var drawn = live.Frame.Drawables.FirstOrDefault(drawable => drawable.Entity == rider);
                var entity = live.World.FindEntity(rider)!;
                TestHarness.Assert(drawn is not null && Math.Abs(drawn.Position.X - (float)entity.X.ToDecimal()) < 1f && Math.Abs(drawn.Position.Y - (float)entity.Y.ToDecimal()) < 1f,
                    $"{skin}: the soldier {rider} is drawn at its station of the Fire Base ({drawn?.Position}, {entity.X}, {entity.Y})");
            }
            var tank = live.World.Entities.First(entity => entity.Template.Name == "AmericaVehicleDozer");
            TestHarness.Assert(live.Frame.Drawables.Any(drawable => drawable.Entity == tank.Id), $"{skin}: what stands outside is drawn as before");
            Click(ScreenOf(fireBase), MouseButton.Left);
            TestHarness.Assert(controller.Input.Selection.Selected.SequenceEqual([fireBase]), $"{skin}: the click selects the Fire Base");

            var commands = controller.BuildDrawList(1280, 720).Commands;
            var bar = controller.CommandBar;
            TestHarness.Assert(bar[1] is { Availability: ButtonAvailability.Available } one && one.Rider == first && bar[2] is { Availability: ButtonAvailability.Available } two && two.Rider == second,
                $"{skin}: places 1 and 2 hold the soldiers ({bar.Context}, {bar[1]?.Button.Name}/{bar[1]?.Rider}, {bar[2]?.Button.Name}/{bar[2]?.Rider})");
            TestHarness.Assert(bar[3] is { Availability: ButtonAvailability.Restricted } && bar[4] is { Availability: ButtonAvailability.Restricted }, $"{skin}: places 3 and 4 are free and disabled");
            foreach (var (slot, template) in new[] { (1, "AmericaInfantryRanger"), (2, "AmericaInfantryMissileDefender") })
                TestHarness.Assert(live.Data.Visuals.TryGet(template, out var visual) && controller.IsWindowShown($"ControlBar.wnd:ButtonCommand{slot:00}")
                                   && commands.OfType<UiImageDraw>().Any(image => image.Destination == controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}") && image.Image == visual.ButtonImage),
                    $"{skin}: place {slot} is shown with the {template}'s cameo");

            var rect = controller.WindowRect("ControlBar.wnd:ButtonCommand01")!.Value;
            Click(new Vector2(rect.CenterX, rect.CenterY), MouseButton.Left);
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 5 && Inside(first); frame++) Frame();
            TestHarness.Assert(!Inside(first) && Inside(second), $"{skin}: a click on place 1 lets the ranger out, the other stays");
        }
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

    // ------------------------------------------------------------------------------------------------ #307 and #308 the colours of a skirmish

    private const string Setup = "SkirmishGameOptionsMenu.wnd:";

    private static void ColorsResolve()
    {
        TestHarness.Assert(MatchDescription.ResolveColors([-1, -1]).SequenceEqual([0, 1]), "two random slots get the first two colours");
        TestHarness.Assert(MatchDescription.ResolveColors([2, 2, -1]).SequenceEqual([2, 0, 1]), "a colour named twice stays with the first slot; the second and the random one get free colours");
        TestHarness.Assert(MatchDescription.ResolveColors([-1, null, 0]).SequenceEqual([1, -1, 0]), "a slot that takes no part holds nothing, a random slot skips a named colour");
    }

    private static GameShell SetupShell(GameData data, UiSkin skin)
    {
        var shell = GameShell.Create(data, new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-colour-tests-" + Guid.NewGuid().ToString("N"))), viewport: new UiViewport(1280, 720), skin: skin);
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSkirmish");
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen, "the skirmish setup is open");
        return shell;
    }

    private static void Choose(GameShell shell, string window, int row)
    {
        var tree = shell.CurrentScreen.Tree;
        tree.Post(UiEventKind.ComboSelected, tree.FindByName(Setup + window)!, value: row);
        tree.Dispatch();
    }

    private static string[] Entries(GameShell shell, string window) => [.. shell.CurrentScreen.Tree.FindByName(Setup + window)!.ComboBox!.Entries];

    private static MatchSetup Start(GameShell shell)
    {
        ShellTestInput.Click(shell, Setup + "ButtonStart");
        shell.BuildDrawList();
        shell.Update(CnCNew.Tests.TestTime.FrameSeconds);
        return shell.DrainOutputs().OfType<StartMatchOutput>().Single().Setup;
    }

    private static void SetupColorBoxes(GameData data)
    {
        var shell = SetupShell(data, SkinPins.Current);
        var screen = (SkirmishSetupScreen)shell.CurrentScreen;
        var names = new ShellSkirmishText(data.Graph, shell.TextSource).ColorNames();
        TestHarness.Assert(screen.ColorOf(0) == -1 && screen.ColorOf(1) == -1, "the person and the computer start at ???");
        TestHarness.Assert(Entries(shell, "ComboBoxColor0").Length == names.Length + 1 && Entries(shell, "ComboBoxColor0")[0] == "???", $"??? and every colour while nobody holds one ({string.Join(",", Entries(shell, "ComboBoxColor0"))})");

        // The computer takes Red (row 2: ???, Gold, Red): the person's box no longer offers it.
        Choose(shell, "ComboBoxColor1", 2);
        TestHarness.Assert(screen.ColorOf(1) == 1, $"the computer holds Red ({screen.ColorOf(1)})");
        TestHarness.Assert(!Entries(shell, "ComboBoxColor0").Contains(names[1]) && Entries(shell, "ComboBoxColor1").Contains(names[1]), $"the person's box lacks Red, the computer's shows it ({string.Join(",", Entries(shell, "ComboBoxColor0"))} / {string.Join(",", Entries(shell, "ComboBoxColor1"))})");
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName(Setup + "ComboBoxColor1")!.ComboBox!.SelectedIndex == 2, "the computer's box shows its Red");

        // The person takes Blue - now the second colour row of a list without Red.
        var blueRow = Array.IndexOf(Entries(shell, "ComboBoxColor0"), names[2]);
        Choose(shell, "ComboBoxColor0", blueRow);
        TestHarness.Assert(screen.ColorOf(0) == 2 && !Entries(shell, "ComboBoxColor1").Contains(names[2]), "the person holds Blue and the computer's box lacks it");

        // A slot that opens gives its colour back (GameSlot::setState); an army that changes its strength keeps it.
        Choose(shell, "ComboBoxPlayer1", 4);
        TestHarness.Assert(screen.ColorOf(1) == 1, "a hard army keeps Red");
        Choose(shell, "ComboBoxPlayer1", 0);
        TestHarness.Assert(screen.ColorOf(1) == -1 && Entries(shell, "ComboBoxColor0").Contains(names[1]), "an open slot gives Red back to the others");
        Choose(shell, "ComboBoxPlayer1", 3);
        TestHarness.Assert(screen.ColorOf(1) == -1, "a new army starts at ???");

        var setup = Start(shell);
        TestHarness.Assert(setup.Details[0].Color == 2 && setup.Details[1].Color == -1, $"the setup carries Blue and ??? ({setup.Details[0].Color}, {setup.Details[1].Color})");
    }

    private static void MatchHouseColors(GameData data)
    {
        var palette = new ShellSkirmishText(data.Graph, null).Colors();
        CnCNew.Data.Definitions.ColorValue Of(int index) => new(palette[index].R, palette[index].G, palette[index].B);

        // The owner's match: two armies whose faction prefers the same blue, both at ???.
        var shell = SetupShell(data, SkinPins.Current);
        var factions = Entries(shell, "ComboBoxPlayerTemplate0");
        Choose(shell, "ComboBoxPlayerTemplate0", Array.IndexOf(factions, "USA"));
        Choose(shell, "ComboBoxPlayerTemplate1", Array.IndexOf(factions, "USA Air Force General"));
        var setup = Start(shell);
        var match = LiveMatch.Create(data, setup with { Options = setup.Options with { MaxTicks = 60 } }, new Vector2(1280, 720));
        var (first, second) = (match.Colors.Of(new PlayerId(1)), match.Colors.Of(new PlayerId(2)));
        TestHarness.Assert(first != second, $"the person and the computer look different ({first}, {second})");
        TestHarness.Assert(first == Of(0) && second == Of(1), $"??? became the first free multiplayer colours, Gold and Red ({first}, {second})");

        // Chosen colours are the ones drawn.
        shell = SetupShell(data, SkinPins.Current);
        Choose(shell, "ComboBoxColor0", 3); // Blue
        Choose(shell, "ComboBoxColor1", 1); // Gold
        setup = Start(shell);
        match = LiveMatch.Create(data, setup with { Options = setup.Options with { MaxTicks = 60 } }, new Vector2(1280, 720));
        TestHarness.Assert(match.Colors.Of(new PlayerId(1)) == Of(2) && match.Colors.Of(new PlayerId(2)) == Of(0), $"Blue and Gold as chosen ({match.Colors.Of(new PlayerId(1))}, {match.Colors.Of(new PlayerId(2))})");
    }

    private static void ColorBoxesShowTheirColor(GameData data)
    {
        var palette = new ShellSkirmishText(data.Graph, null).Colors();
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
        {
            var shell = SetupShell(data, skin);
            var names = new ShellSkirmishText(data.Graph, shell.TextSource).ColorNames();
            Choose(shell, "ComboBoxColor1", 2); // Red
            var box = shell.CurrentScreen.Tree.FindByName(Setup + "ComboBoxColor1")!;
            var texts = shell.BuildDrawList().Commands.OfType<UiTextDraw>().ToArray();
            TestHarness.Assert(texts.Any(text => text.Text == names[1] && text.Color == palette[1] && box.Rect.Contains(text.X, text.Y)), $"{skin.Id}: the box writes Red in red");
            TestHarness.Assert(texts.Any(text => text.Text == "???" && text.Color == new WndColor(255, 255, 255, 255)), $"{skin.Id}: ??? is written in white");

            var button = UiGadgetGeometry.ComboButton(box);
            ShellTestInput.ClickAt(shell, button.CenterX, button.CenterY);
            TestHarness.Assert(shell.CurrentScreen.Tree.OpenComboBox == box, $"{skin.Id}: the colour list opens");
            var rows = shell.BuildDrawList().Commands.OfType<UiTextDraw>().ToArray();
            var shown = Math.Min(UiGadgetGeometry.ComboVisibleRows(box), box.ComboBox!.Entries.Count) - 1;
            for (var color = 0; color < shown; color++)
                TestHarness.Assert(rows.Any(text => text.Text == names[color] && text.Color == palette[color]), $"{skin.Id}: the row {names[color]} is written in its colour");
            ShellTestInput.ClickAt(shell, button.CenterX, button.CenterY);
        }
    }
}

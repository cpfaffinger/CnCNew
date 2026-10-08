using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Presentation;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Package C3: input events, selection, command modes, command bar, hotkeys and camera input.</summary>
static class InputTests
{
    public static int Run(string[] args)
    {
        var run = new InputRun();
        run.Test("Input: click, drag threshold, double click, right-click time limit, focus loss", InputStateGestures);
        run.Test("Input: CommandMap parsing, unknown names, conflicts, rebind and settings round trip", HotkeyMapParsing);
        run.Test("Input: CommandMap from the compiled graph equals the text parser", HotkeyMapFromGraph);
        run.Test("Input: modifier-only bindings and control-bar hotkeys through the dispatcher", HotkeyDispatch);
        run.Test("Input: pick rules (radius, minimum, nearest to camera, visibility, containment)", PickRules);
        run.Test("Input: box and click selection precedence, shift add and toggle, foreign info selection", SelectionPrecedence);
        run.Test("Input: double click, select matching, select all and next unit", SelectionMatching);
        run.Test("Input: control groups create, recall, add, double press and pruning", ControlGroups);
        run.Test("Input: contextual orders produce the exact commands", ContextOrders);
        run.Test("Input: standing orders attack, attack-move, guard, waypoints, stop and scatter", StandingOrderBehaviour);
        run.Test("Input: placement validity, ghost rotation and the construct command", PlacementRules);
        run.Test("Input: a placement starts at the template's PlacementViewAngle and a click without a drag keeps it", PlacementViewAngle);
        run.Test("Input: command bar slots, availability reasons, queue and multi-select", CommandBarStates);
        run.Test("Input: camera intent from keys, edges, drag, wheel and metas", CameraIntents);
        run.Test("Input: acceptance sequence through the event pipeline is replayable", AcceptanceSequence);
        run.Test("Input: the alternate mouse setup is the default, the original's setup stays selectable", MouseSchemes);
        var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
        if (contentDirectory is not null) run.Test("Input real data: CommandMap, command buttons and command bars of shipped objects", () => RealData(contentDirectory));
        return run.Finish();
    }

    // ============================================================================================ fixture

    internal static readonly PlayerId Me = new(1);
    internal static readonly PlayerId Enemy = new(2);

    internal const string CommandMapText = """
        ; synthetic command map
        CommandMap STOP
          Key = KEY_S
          Transition = DOWN
          Modifiers = NONE
          UseableIn = GAME
          Category = CONTROL
        End
        CommandMap SCATTER
          Key = KEY_X
          Transition = DOWN
          Modifiers = NONE
          UseableIn = GAME
        End
        CommandMap CREATE_TEAM1
          Key = KEY_1
          Transition = DOWN
          Modifiers = CTRL
          UseableIn = GAME
          Category = TEAM
        End
        CommandMap SELECT_TEAM1
          Key = KEY_1
          Transition = DOWN
          Modifiers = NONE
          UseableIn = GAME
          Category = TEAM
        End
        CommandMap ADD_TEAM1
          Key = KEY_1
          Transition = DOWN
          Modifiers = SHIFT
          UseableIn = GAME
        End
        CommandMap SELECT_TEAM2
          Key = KEY_2
          Transition = DOWN
          Modifiers = NONE
          UseableIn = GAME
        End
        CommandMap CREATE_TEAM2
          Key = KEY_2
          Transition = DOWN
          Modifiers = CTRL
          UseableIn = GAME
        End
        CommandMap BEGIN_FORCEATTACK
          Key = KEY_NONE
          Transition = DOWN
          Modifiers = CTRL
          UseableIn = GAME
        End
        CommandMap END_FORCEATTACK
          Key = KEY_NONE
          Transition = UP
          Modifiers = CTRL
          UseableIn = GAME
        End
        CommandMap BEGIN_WAYPOINTS
          Key = KEY_NONE
          Transition = DOWN
          Modifiers = ALT
          UseableIn = GAME
        End
        CommandMap END_WAYPOINTS
          Key = KEY_NONE
          Transition = UP
          Modifiers = ALT
          UseableIn = GAME
        End
        CommandMap BEGIN_PREFER_SELECTION
          Key = KEY_NONE
          Transition = DOWN
          Modifiers = SHIFT
          UseableIn = GAME
        End
        CommandMap END_PREFER_SELECTION
          Key = KEY_NONE
          Transition = UP
          Modifiers = SHIFT
          UseableIn = GAME
        End
        CommandMap SELECT_ALL
          Key = KEY_Q
          Transition = DOWN
          Modifiers = NONE
          UseableIn = GAME
        End
        CommandMap SELECT_MATCHING_UNITS
          Key = KEY_E
          Transition = Down
          Modifiers = NONE
          UseableIn = GAME
        End
        CommandMap OPTIONS
          Key = KEY_ESC
          Transition = DOWN
          Modifiers = NONE
          UseableIn = GAME SHELL
          DisplayName = GUI:Options
        End
        CommandMap BEGIN_CAMERA_ROTATE_LEFT
          Key = KEY_KP4
          Transition = DOWN
          Modifiers = NONE
          UseableIn = GAME
        End
        CommandMap END_CAMERA_ROTATE_LEFT
          Key = KEY_KP4
          Transition = UP
          Modifiers = NONE
          UseableIn = GAME
        End
        CommandMap CAMERA_RESET
          Key = KEY_KP5
          Transition = DOWN
          Modifiers = NONE
          UseableIn = GAME
        End
        """;

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/English/CommandMap.ini"] = CommandMapText,
        ["Data/INI/Upgrade.ini"] = """
        Upgrade Upgrade_Flash
          Type = PLAYER
          BuildCost = 300
          BuildTime = 1
        End
        """,
        ["Data/INI/CommandButton.ini"] = """
        CommandButton Command_AttackMove
          Command = ATTACK_MOVE
          Options = OK_FOR_MULTI_SELECT NEED_TARGET_POS
          TextLabel = CONTROLBAR:AttackMove
          ButtonImage = SSAttackMove
          CursorName = AttackMove
          InvalidCursorName = GenericInvalid
        End
        CommandButton Command_Guard
          Command = GUARD
          Options = OK_FOR_MULTI_SELECT NEED_TARGET_POS
          TextLabel = CONTROLBAR:Guard
          ButtonImage = SSGuard
        End
        CommandButton Command_Stop
          Command = STOP
          Options = OK_FOR_MULTI_SELECT
          TextLabel = CONTROLBAR:Stop
          ButtonImage = SSStop
        End
        CommandButton Command_Sell
          Command = SELL
          TextLabel = CONTROLBAR:Sell
          ButtonImage = SSSell
        End
        CommandButton Command_SetRallyPoint
          Command = SET_RALLY_POINT
          Options = NEED_TARGET_POS
          TextLabel = CONTROLBAR:SetRallyPoint
          ButtonImage = SSRally
          CursorName = SetRallyPoint
        End
        CommandButton Command_ConstructPlant
          Command = DOZER_CONSTRUCT
          Object = Plant
          TextLabel = CONTROLBAR:ConstructPlant
          ButtonImage = SPlant
        End
        CommandButton Command_ConstructBarracks
          Command = DOZER_CONSTRUCT
          Object = Barracks
          TextLabel = CONTROLBAR:ConstructBarracks
          ButtonImage = SBarracks
        End
        CommandButton Command_ConstructTower
          Command = DOZER_CONSTRUCT
          Object = Tower
          TextLabel = CONTROLBAR:ConstructTower
          ButtonImage = STower
        End
        CommandButton Command_TrainRanger
          Command = UNIT_BUILD
          Object = Ranger
          TextLabel = CONTROLBAR:TrainRanger
          ButtonImage = SRanger
        End
        CommandButton Command_UpgradeFlash
          Command = PLAYER_UPGRADE
          Upgrade = Upgrade_Flash
          TextLabel = CONTROLBAR:UpgradeFlash
          ButtonImage = SFlash
        End
        """,
        ["Data/INI/CommandSet.ini"] = """
        CommandSet RangerCommandSet
          11 = Command_AttackMove
          13 = Command_Guard
          14 = Command_Stop
        End
        CommandSet TankCommandSet
          11 = Command_AttackMove
          12 = Command_Guard
          14 = Command_Stop
        End
        CommandSet DozerCommandSet
          1 = Command_ConstructPlant
          2 = Command_ConstructBarracks
          3 = Command_ConstructTower
          14 = Command_Stop
        End
        CommandSet PlantCommandSet
          14 = Command_Sell
        End
        CommandSet BarracksCommandSet
          1 = Command_TrainRanger
          2 = Command_UpgradeFlash
          13 = Command_SetRallyPoint
          14 = Command_Sell
        End
        """,
        ["Data/INI/Weapon.ini"] = """
        Weapon TestWeapon
          PrimaryDamage = 10
        End
        Weapon TestGun
          PrimaryDamage = 10
          AttackRange = 50.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 100
        End
        """,
        ["Data/INI/Object/InputFixture.ini"] = """
        Locomotor GroundLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 60.0
          Acceleration = 60.0
          Braking = 60.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Object Ranger
          KindOf = SELECTABLE INFANTRY
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          Geometry = CYLINDER
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 3.0
          GeometryHeight = 6.0
          BuildCost = 200
          BuildTime = 1
          CommandSet = RangerCommandSet
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TestGun
          End
        End

        Object Tank
          KindOf = SELECTABLE VEHICLE
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          GeometryHeight = 6.0
          CommandSet = TankCommandSet
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
            MoodAttackCheckRate = 100
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TestGun
          End
        End

        Object Dozer
          KindOf = SELECTABLE VEHICLE DOZER
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          Geometry = CYLINDER
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 4.0
          CommandSet = DozerCommandSet
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
          End
        End

        Object Hauler
          KindOf = SELECTABLE VEHICLE HARVESTER
          VisionRange = 50.0
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SupplyTruckAIUpdate AI01
            MaxBoxes = 2
            SupplyCenterActionDelay = 100
            SupplyWarehouseActionDelay = 100
            SupplyWarehouseScanDistance = 400.0
          End
        End

        Object Plant
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 100
          BuildTime = 2
          EnergyProduction = 5
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          CommandSet = PlantCommandSet
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Barracks
          KindOf = SELECTABLE STRUCTURE POWERED
          BuildCost = 100
          BuildTime = 1
          EnergyProduction = -2
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          CommandSet = BarracksCommandSet
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 2
          End
        End

        Object Tower
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 50
          BuildTime = 1
          Geometry = BOX
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
          Prerequisites
            Object = Plant
          End
        End

        Object Bunker
          KindOf = SELECTABLE STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 8.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 2
          End
        End

        Object Depot
          KindOf = SELECTABLE STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SupplyCenterDockUpdate Dock01
            NumberApproachPositions = 2
          End
          Behavior = SupplyCenterCreate Create01
          End
        End
        """,
    };

    private static readonly Lazy<SyntheticFixture> SharedFixture = new(() => new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: Files()));

    internal static SyntheticFixture Fixture => SharedFixture.Value;

    /// <summary>A 20 x 20 cell (200 m) flat map with one cliff cell at (15, 15).</summary>
    internal static TerrainMap FlatTerrain()
    {
        const int cells = 20, border = 1;
        var width = cells + 2 * border;
        var stride = (width + 7) / 8;
        var flags = new byte[stride * width];
        var (cx, cy) = (15 + border, 15 + border);
        flags[cy * stride + (cx >> 3)] |= (byte)(1 << (cx & 7));
        var blend = new MapBlendTiles(8, [], [], [], [], [..flags], stride, 0, 0, [], [], [], []);
        var map = new MapData("input", new MapHeightField(width, width, border, [new MapBoundary(cells, cells)], [..new byte[width * width]]), blend,
            MapProperties.Empty, [], [], [], [], [], [], []);
        return TerrainMap.Build(map, "input");
    }

    /// <summary>
    /// Orthographic top-down test camera: 2 pixels per metre, 800 x 600 viewport, screen y grows downwards while world y grows upwards:
    /// world (x, y, z) is screen (2x, 600 - 2y), whatever z. The view ray starts 1000 m up, so the depth of a point is 1000 - z.
    /// </summary>
    internal sealed class TopDownView(float pixelsPerMetre = 2f, float width = 800f, float height = 600f) : IViewProjection
    {
        public Matrix4x4 ViewProjection => Matrix4x4.Identity;
        public Vector2 ViewportSize => new(width, height);

        public bool TryScreenToGround(Vector2 screen, out Vector3 world)
        {
            world = new Vector3(screen.X / pixelsPerMetre, (height - screen.Y) / pixelsPerMetre, 0);
            return true;
        }

        public bool TryWorldToScreen(Vector3 world, out Vector2 screen)
        {
            screen = new Vector2(world.X * pixelsPerMetre, height - world.Y * pixelsPerMetre);
            return true;
        }

        public void GetScreenRay(Vector2 screen, out Vector3 origin, out Vector3 direction)
        {
            TryScreenToGround(screen, out var ground);
            origin = ground with { Z = 1000 };
            direction = -Vector3.UnitZ;
        }
    }

    internal static Vector2 Screen(float x, float y) => new(2 * x, 600 - 2 * y);

    /// <summary>Submits to the world like <c>SkirmishMatchSession.Issue</c> (per-player sequence, current tick) and records every command.</summary>
    internal sealed class WorldSink(SimulationWorld world, List<SimCommand> log) : ICommandSink
    {
        private ulong _sequence;
        public List<SimCommand> Issued { get; } = [];

        public SubmitResult Issue(Func<ulong, long, SimCommand> make)
        {
            var command = make(++_sequence, world.Tick);
            Issued.Add(command);
            log.Add(command);
            return world.Submit(command);
        }
    }

    internal sealed class Arena
    {
        private ulong _system;

        public Arena(int money = 1000, bool terrain = true)
        {
            Terrain = terrain ? FlatTerrain() : null;
            Players = [new PlayerConfiguration(Me, "Me"), new PlayerConfiguration(Enemy, "Enemy")];
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), CnCNew.Tests.TestRate.Default, 7, Players, SyntheticFixture.FixtureId, Fixture.Fingerprint, Terrain?.Fingerprint ?? ""), Fixture.Templates, Terrain);
            Sink = new WorldSink(World, Log);
            if (money > 0) System(sequence => new GiveMoneyCommand(PlayerId.System, sequence, World.Tick, Me, Fixed.FromInt(money)));
        }

        public TerrainMap? Terrain { get; }
        public ImmutableArray<PlayerConfiguration> Players { get; }
        public SimulationWorld World { get; }
        public List<SimCommand> Log { get; } = [];
        public WorldSink Sink { get; }

        public void System(Func<ulong, SimCommand> make)
        {
            var command = make(++_system);
            Log.Add(command);
            Assert(World.Submit(command).Accepted, $"system command {command.GetType().Name} accepted");
        }

        public void Spawn(string template, PlayerId owner, float x, float y)
            => System(sequence => new SpawnCommand(PlayerId.System, sequence, World.Tick, template, owner, SimulationQueries.ToFixed(x), SimulationQueries.ToFixed(y), Fixed.Zero, Fixed.Zero));

        public void Step(int ticks = 1)
        {
            for (var tick = 0; tick < ticks; tick++) World.Step();
        }

        public Entity Find(string template, PlayerId owner, float x, float y)
            => World.Entities.Where(entity => entity.Template.Name == template && entity.Owner == owner
                                              && Math.Abs((float)entity.X.ToDecimal() - x) < 0.01f && Math.Abs((float)entity.Y.ToDecimal() - y) < 0.01f)
                .OrderBy(entity => entity.Id.Value).First();

        public ReplayFile Replay(long endTick)
        {
            var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, World.Time.TicksPerSecond, Fixture.Fingerprint, SyntheticFixture.FixtureId, 7,
                new WorldId(1), Players, endTick, 0, Terrain?.Fingerprint ?? "");
            return new ReplayFile(header, [..Log.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence)], []);
        }
    }

    // ============================================================================================ input state

    private static void InputStateGestures()
    {
        var state = new InputState();
        Assert(state.Settings.DragTolerancePixels == 25f && state.Settings.ClickMaxMilliseconds == 250, "Mouse.ini defaults: DragTolerance 25 px, DragToleranceMS 250");
        state.Apply(new MouseButtonEvent(0, MouseButton.Left, true, new Vector2(100, 100)));
        Assert(state.Apply(new MouseMoveEvent(5, new Vector2(125, 100))).IsEmpty, "25 px on one axis is still a click");
        var started = state.Apply(new MouseMoveEvent(6, new Vector2(126, 100)));
        Assert(started is [{ Kind: GestureKind.DragStarted, Button: MouseButton.Left }], "more than 25 px starts a drag");
        var ended = state.Apply(new MouseButtonEvent(7, MouseButton.Left, false, new Vector2(140, 150)));
        Assert(ended is [{ Kind: GestureKind.DragMoved }, { Kind: GestureKind.DragEnded } last] && last.Rect == new ScreenRect(new Vector2(100, 100), new Vector2(140, 150)), "the release moves the drag, then ends it with the rectangle");

        state.Apply(new MouseButtonEvent(1000, MouseButton.Left, true, new Vector2(10, 10)));
        var first = state.Apply(new MouseButtonEvent(1050, MouseButton.Left, false, new Vector2(10, 10)));
        state.Apply(new MouseButtonEvent(1400, MouseButton.Left, true, new Vector2(12, 11)));
        var second = state.Apply(new MouseButtonEvent(1450, MouseButton.Left, false, new Vector2(12, 11)));
        Assert(first is [{ Kind: GestureKind.Click }] && second is [{ Kind: GestureKind.DoubleClick }], "a second click 350 ms later within 4 px is a double click");
        state.Apply(new MouseButtonEvent(1500, MouseButton.Left, true, new Vector2(12, 11)));
        var third = state.Apply(new MouseButtonEvent(1520, MouseButton.Left, false, new Vector2(12, 11)));
        Assert(third is [{ Kind: GestureKind.Click }], "a double click consumes the pair");
        state.Apply(new MouseButtonEvent(3000, MouseButton.Left, true, new Vector2(12, 11)));
        state.Apply(new MouseButtonEvent(3010, MouseButton.Left, false, new Vector2(12, 11)));
        state.Apply(new MouseButtonEvent(3511, MouseButton.Left, true, new Vector2(12, 11)));
        Assert(state.Apply(new MouseButtonEvent(3520, MouseButton.Left, false, new Vector2(12, 11))) is [{ Kind: GestureKind.Click }], "501 ms apart is two clicks");

        state.Apply(new MouseButtonEvent(4000, MouseButton.Right, true, new Vector2(50, 50)));
        Assert(state.Apply(new MouseButtonEvent(4251, MouseButton.Right, false, new Vector2(50, 50))).IsEmpty, "a right press held 251 ms is no click");
        state.Apply(new MouseButtonEvent(5000, MouseButton.Right, true, new Vector2(50, 50)));
        Assert(state.Apply(new MouseButtonEvent(5250, MouseButton.Right, false, new Vector2(50, 50))) is [{ Kind: GestureKind.Click, Button: MouseButton.Right }], "250 ms is a right click");

        state.Apply(new KeyEvent(6000, KeyCode.LeftControl, true));
        state.Apply(new KeyEvent(6001, KeyCode.RightShift, true));
        Assert(state.Modifiers == (KeyModifiers.Control | KeyModifiers.Shift), "modifiers follow the held modifier keys");
        state.Apply(new MouseButtonEvent(6002, MouseButton.Left, true, new Vector2(0, 0)));
        state.Apply(new MouseMoveEvent(6003, new Vector2(100, 0)));
        var lost = state.Apply(new FocusLostEvent(6004));
        Assert(lost is [{ Kind: GestureKind.DragCancelled }] && state.Modifiers == KeyModifiers.None && !state.IsButtonDown(MouseButton.Left), "focus loss cancels the drag and releases everything");
    }

    // ============================================================================================ hotkeys

    private static void HotkeyMapParsing()
    {
        var map = HotkeyMap.Parse(CommandMapText);
        Assert(map.Issues.IsEmpty, $"the synthetic map parses cleanly: {string.Join("; ", map.Issues)}");
        Assert(map.Entries.Length == 19, $"19 entries ({map.Entries.Length})");
        Assert(map.TryGetEntry(MetaAction.CreateTeam1, out var create) && create.Binding == new KeyBinding(KeyCode.D1, KeyModifiers.Control) && create.Category == "TEAM", "CREATE_TEAM1 = Ctrl+1");
        Assert(map.TryGetEntry(MetaAction.SelectMatchingUnits, out var matching) && matching.Binding.Transition == KeyTransition.Down, "transition names ignore case (Down)");
        Assert(map.TryGetEntry(MetaAction.Options, out var options) && options.UsableIn == (HotkeyContexts.Game | HotkeyContexts.Shell) && options.DisplayNameLabel == "GUI:Options", "UseableIn is a bit list");
        Assert(map.Conflicts.IsEmpty, "no conflicts in the synthetic map");
        Assert(map.Resolve(new KeyBinding(KeyCode.None, KeyModifiers.Control), HotkeyContexts.Game) is [MetaAction.BeginForceAttack], "KEY_NONE + CTRL down is BEGIN_FORCEATTACK");
        Assert(map.Resolve(new KeyBinding(KeyCode.Escape, KeyModifiers.None), HotkeyContexts.Shell) is [MetaAction.Options], "shell context");
        Assert(map.Resolve(new KeyBinding(KeyCode.S, KeyModifiers.None), HotkeyContexts.Shell).IsEmpty, "STOP is game only");

        var broken = HotkeyMap.Parse("""
            CommandMap NOT_A_META
              Key = KEY_A
            End
            CommandMap STOP
              Key = KEY_WHATEVER
            End
            CommandMap SCATTER
              Key = KEY_X
              Colour = Red
            End
            CommandMap SELECT_ALL
              Transition = DOWN
            End
            CommandMap CAMERA_RESET
              Key = KEY_R
              Modifiers = HYPER
            End
            CommandMap DIPLOMACY
              Key = KEY_TAB
              UseableIn = EVERYWHERE
            End
            stray line
            """);
        var kinds = broken.Issues.Select(issue => issue.Kind).ToList();
        Assert(broken.Entries.IsEmpty, "every broken entry is skipped");
        Assert(kinds.SequenceEqual([HotkeyIssueKind.Syntax, HotkeyIssueKind.UnknownAction, HotkeyIssueKind.UnknownKey, HotkeyIssueKind.UnknownField,
            HotkeyIssueKind.MissingKey, HotkeyIssueKind.UnknownModifiers, HotkeyIssueKind.UnknownContext]), $"all seven problems are reported once each: {string.Join(", ", kinds)}");
        Assert(kinds.Contains(HotkeyIssueKind.UnknownAction) && kinds.Contains(HotkeyIssueKind.UnknownKey) && kinds.Contains(HotkeyIssueKind.UnknownField)
               && kinds.Contains(HotkeyIssueKind.MissingKey) && kinds.Contains(HotkeyIssueKind.UnknownModifiers) && kinds.Contains(HotkeyIssueKind.UnknownContext)
               && kinds.Contains(HotkeyIssueKind.Syntax), "each issue kind appears");

        var conflicting = map.Rebind(MetaAction.Scatter, new KeyBinding(KeyCode.S, KeyModifiers.None));
        Assert(conflicting.Conflicts is [{ Actions: [MetaAction.Scatter, MetaAction.Stop] }] && conflicting.Conflicts[0].Binding == new KeyBinding(KeyCode.S, KeyModifiers.None),
            "rebinding scatter onto S is reported as a conflict with STOP");
        Assert(conflicting.Resolve(new KeyBinding(KeyCode.S, KeyModifiers.None), HotkeyContexts.Game) is [MetaAction.Scatter, MetaAction.Stop], "both fire, in action order, never silently resolved");
        var shellOnly = map.Rebind(MetaAction.Scatter, new KeyBinding(KeyCode.Escape, KeyModifiers.None));
        Assert(shellOnly.Conflicts.Length == 1, "Escape is shared by OPTIONS (game+shell) and scatter (game)");

        var settings = conflicting.Unbind(MetaAction.CameraReset).WithEngineDefaults().ToSettings();
        Assert(settings["STOP"] == "NONE KEY_S DOWN" && settings["CREATE_TEAM1"] == "CTRL KEY_1 DOWN" && settings["END_FORCEATTACK"] == "CTRL KEY_NONE UP", "settings spell bindings as MODIFIERS KEY TRANSITION");
        Assert(settings["CAMERA_RESET"] == HotkeyMap.Unbound && settings["ENGINE_ATTACK_MOVE"] == HotkeyMap.Unbound && settings["ENGINE_CAMERA_SCROLL_UP"] == "NONE KEY_UP DOWN", "unbound and engine actions");
        Assert(settings.Count == Enum.GetValues<MetaAction>().Length, "every action has a settings entry");
        var restored = map.ApplySettings(settings);
        Assert(restored.ToSettings().SequenceEqual(settings), "the settings round-trip");
        Assert(restored.TryGetEntry(MetaAction.Scatter, out var scatter) && scatter.Source == HotkeySource.User && !restored.TryGetEntry(MetaAction.CameraReset, out _), "user bindings are marked");
        var bad = map.ApplySettings(new Dictionary<string, string> { ["STOP"] = "CTRL KEY_BLAH DOWN", ["WARP_DRIVE"] = "NONE KEY_W DOWN" });
        Assert(bad.Issues.Select(issue => issue.Kind).Order().SequenceEqual([HotkeyIssueKind.UnknownAction, HotkeyIssueKind.BadSettingValue]) && bad.TryGetEntry(MetaAction.Stop, out var stop)
               && stop.Binding.Key == KeyCode.S, "bad settings are reported and leave the binding alone");
    }

    private static void HotkeyMapFromGraph()
    {
        var fromGraph = HotkeyMap.FromGraph(Fixture.Graph);
        var fromText = HotkeyMap.Parse(CommandMapText);
        Assert(fromGraph.Issues.IsEmpty && fromGraph.Entries.SequenceEqual(fromText.Entries), "the graph's compatibility definitions give the same map");
    }

    private static void HotkeyDispatch()
    {
        var arena = new Arena();
        arena.Spawn("Ranger", Me, 40, 40);
        arena.Spawn("Barracks", Me, 150, 150);
        arena.Step(2);
        var view = new TopDownView();
        var buttons = CommandButtonCatalog.Build(Fixture.Graph);
        var labels = new Dictionary<string, string> { ["CONTROLBAR:AttackMove"] = "&Attack Move", ["CONTROLBAR:Stop"] = "&Stop", ["CONTROLBAR:Guard"] = "&Guard" };
        var input = new GameInputController(Me, arena.Sink, HotkeyMap.Parse(CommandMapText), buttons, label => labels.GetValueOrDefault(label));
        var requests = new List<MetaAction>();
        input.UiRequest += requests.Add;
        var ranger = arena.Find("Ranger", Me, 40, 40);
        input.Selection.Replace([ranger.Id]);

        input.Process(new KeyEvent(0, KeyCode.LeftControl, true), arena.World, view);
        Assert(input.Commands.ForceAttack, "Ctrl alone begins force attack");
        input.Process(new KeyEvent(1, KeyCode.LeftShift, true), arena.World, view);
        Assert(!input.Commands.PreferSelection && input.Commands.ForceAttack, "Shift under Ctrl is the SHIFT_CTRL state, which nothing binds");
        input.Process(new KeyEvent(2, KeyCode.LeftControl, false), arena.World, view);
        Assert(!input.Commands.ForceAttack && !input.Commands.PreferSelection, "releasing Ctrl first ends force attack (release order does not matter)");
        input.Process(new KeyEvent(3, KeyCode.LeftShift, false), arena.World, view);
        input.Process(new KeyEvent(4, KeyCode.RightShift, true), arena.World, view);
        Assert(input.Commands.PreferSelection, "Shift alone begins prefer-selection");
        input.Process(new KeyEvent(5, KeyCode.RightShift, false), arena.World, view);
        Assert(!input.Commands.PreferSelection, "and its release ends it");

        input.Process(new KeyEvent(10, KeyCode.A, true), arena.World, view);
        input.Process(new KeyEvent(11, KeyCode.A, false), arena.World, view);
        Assert(input.Commands.Mode == CommandModeKind.AttackMove, "A (the &A of the attack-move label) activates the control-bar button on key up");
        input.Process(new KeyEvent(12, KeyCode.Escape, true), arena.World, view);
        Assert(input.Commands.Mode == CommandModeKind.None && requests.Count == 0, "Escape first cancels the pending mode");
        input.Process(new KeyEvent(13, KeyCode.Escape, false), arena.World, view);
        input.Process(new KeyEvent(14, KeyCode.Escape, true), arena.World, view);
        Assert(requests is [MetaAction.Options], "a second Escape opens the options");

        var before = arena.Sink.Issued.Count;
        input.Process(new KeyEvent(20, KeyCode.S, true), arena.World, view);
        input.Process(new KeyEvent(21, KeyCode.S, false), arena.World, view);
        Assert(arena.Sink.Issued.Count == before + 1 && arena.Sink.Issued[^1] is SetMoveTargetCommand, "S stops once: the meta fires, the Stop button hotkey is suppressed");
        input.Process(new KeyEvent(30, KeyCode.LeftControl, true), arena.World, view);
        input.Process(new KeyEvent(31, KeyCode.A, true), arena.World, view);
        input.Process(new KeyEvent(32, KeyCode.A, false), arena.World, view);
        input.Process(new KeyEvent(33, KeyCode.LeftControl, false), arena.World, view);
        Assert(input.Commands.Mode == CommandModeKind.None, "a control-bar hotkey needs the key without modifiers");

        // A key CommandMap.ini binds to a meta on the key-down is still the hot key of another button on the key-up (MetaEventTranslator destroys only the message whose
        // transition matches): with &S in a construct label, S starts the placement although STOP fired on the press. That is how the Scorpion, the Sentry Drone, the
        // Listening Outpost and the Saboteur are built by key in the retail data.
        arena.Spawn("Dozer", Me, 60, 70);
        arena.Step(2);
        var builder = new GameInputController(Me, arena.Sink, HotkeyMap.Parse(CommandMapText), buttons, label => label == "CONTROLBAR:ConstructPlant" ? "Con&struct Plant" : null);
        builder.Selection.Replace([arena.Find("Dozer", Me, 60, 70).Id]);
        Assert(!HotkeyMap.Parse(CommandMapText).Resolve(new KeyBinding(KeyCode.S, KeyModifiers.None), HotkeyContexts.Game).IsEmpty, "S is bound to a meta action on the key-down");
        builder.Process(new KeyEvent(40, KeyCode.S, true), arena.World, view);
        Assert(builder.Commands.Mode == CommandModeKind.None, "the press alone starts nothing");
        builder.Process(new KeyEvent(41, KeyCode.S, false), arena.World, view);
        Assert(builder.Commands is { Mode: CommandModeKind.Placement, PendingButton.Object: "Plant" }, $"the release presses the construct button with &S ({builder.Commands.Mode})");
    }

    // ============================================================================================ picking and selection

    private static void PickRules()
    {
        var arena = new Arena();
        arena.Spawn("Ranger", Me, 40, 40);       // radius 3 m = 6 px, below the 8 px minimum
        arena.Spawn("Tank", Me, 60, 40);         // radius 6 m = 12 px
        arena.Spawn("Ranger", Me, 70, 40);       // overlaps the tank's pick circle
        arena.Spawn("Ranger", Enemy, 150, 40);   // in sight of the own units (vision 100)
        arena.Spawn("Ranger", Enemy, 190, 190);  // far outside every vision range
        arena.Spawn("Bunker", Me, 100, 100);    // 8 m high: its centre is 1 m closer to the camera than a ranger's
        arena.Spawn("Ranger", Me, 104, 100);
        arena.Step(2);
        var view = new TopDownView();
        var picker = new EntityPicker();
        var world = arena.World;
        var ranger = arena.Find("Ranger", Me, 40, 40);
        var tank = arena.Find("Tank", Me, 60, 40);
        var near = arena.Find("Ranger", Me, 70, 40);
        Assert(picker.TryProject(view, tank, out var projected) && projected.Center == Screen(60, 40) && projected.Radius == 12f && projected.Depth == 997f,
            "the tank projects to its screen centre, 12 px radius, depth 1000 - 3");
        Assert(picker.Pick(world, Me, view, Screen(40, 40) + new Vector2(8, 0)) == ranger, "8 px from a small unit still picks it (minimum radius)");
        Assert(picker.Pick(world, Me, view, Screen(40, 40) + new Vector2(9, 0)) is null, "9 px misses it");
        Assert(picker.Pick(world, Me, view, Screen(60, 40) - new Vector2(12, 0)) == tank, "12 px picks the tank");
        Assert(picker.Pick(world, Me, view, Screen(60, 40) - new Vector2(13, 0)) is null, "13 px misses it");
        // At 66 m the tank is 12 px away (radius 12) and the ranger at 70 m 8 px away (radius 8): both contain the cursor.
        Assert(picker.Pick(world, Me, view, Screen(66, 40)) == near, "equal depth: the smaller screen distance wins");
        Assert(picker.Pick(world, Me, view, Screen(103, 100)) == arena.Find("Bunker", Me, 100, 100), "the bunker is nearer the camera than the ranger 2 px away: the view ray hits it first");
        Assert(picker.Pick(world, Me, view, Screen(150, 40)) == arena.Find("Ranger", Enemy, 150, 40), "a visible enemy is pickable");
        Assert(picker.Pick(world, Me, view, Screen(190, 190)) is null, "an enemy in the shroud is not pickable");
        var bunker = arena.Find("Bunker", Me, 100, 100);
        Assert(picker.Pick(world, Me, view, Screen(100, 100)) == bunker, "a selectable structure is pickable");
        arena.Sink.Issue((sequence, tick) => new EnterContainerCommand(Me, sequence, tick, ranger.Id, bunker.Id));
        arena.Step();
        Assert(ranger.ContainedBy == bunker.Id && picker.Pick(world, Me, view, Screen(40, 40)) is null && !EntityPicker.IsSelectable(ranger), "a contained unit is not pickable");
        var inBox = picker.InRect(world, Me, view, ScreenRect.FromCorners(Screen(0, 0), Screen(200, 200)));
        Assert(inBox.Select(entity => entity.Id.Value).SequenceEqual(inBox.Select(entity => entity.Id.Value).Order()) && inBox.Length == 5, $"the box lists the five pickable objects in id order ({inBox.Length})");
    }

    private static void SelectionPrecedence()
    {
        var arena = new Arena();
        arena.Spawn("Ranger", Me, 40, 40);
        arena.Spawn("Ranger", Me, 46, 40);
        arena.Spawn("Plant", Me, 40, 70);
        arena.Spawn("Barracks", Me, 80, 70);
        arena.Spawn("Ranger", Enemy, 120, 40);
        arena.Spawn("Ranger", Enemy, 130, 40);
        arena.Step(2);
        var view = new TopDownView();
        var world = arena.World;
        var selection = new SelectionModel(Me);
        var changes = new List<ImmutableArray<EntityId>>();
        selection.SelectionChanged += changes.Add;
        var (r1, r2) = (arena.Find("Ranger", Me, 40, 40), arena.Find("Ranger", Me, 46, 40));
        var (plant, barracks) = (arena.Find("Plant", Me, 40, 70), arena.Find("Barracks", Me, 80, 70));
        var (e1, e2) = (arena.Find("Ranger", Enemy, 120, 40), arena.Find("Ranger", Enemy, 130, 40));
        ScreenRect Box(float x0, float y0, float x1, float y1) => ScreenRect.FromCorners(Screen(x0, y0), Screen(x1, y1));

        Assert(selection.BoxSelect(world, view, Box(30, 30, 50, 80), false) && selection.Selected.SequenceEqual([r1.Id, r2.Id]), "units win over the building in the same box");
        Assert(selection.IsCommandable(world) && changes.Count == 1, "an own selection is commandable and raised one change");
        Assert(selection.BoxSelect(world, view, Box(30, 60, 50, 80), false) && selection.Selected.SequenceEqual([plant.Id]), "a box holding one building selects it");
        Assert(selection.BoxSelect(world, view, Box(30, 60, 90, 80), false) && selection.IsEmpty, "a box with two own buildings and no unit clears the selection (the original's quirk)");
        Assert(!selection.BoxSelect(world, view, Box(0, 150, 20, 190), false) && selection.IsEmpty, "an empty box changes nothing");
        selection.Replace([r1.Id]);
        Assert(selection.BoxSelect(world, view, Box(44, 30, 48, 50), true) && selection.Selected.SequenceEqual([r1.Id, r2.Id]), "shift + box adds");
        Assert(selection.ClickSelect(world, view, Screen(46, 40), true) && selection.Selected.SequenceEqual([r1.Id]), "shift + click on a selected unit removes it");
        Assert(selection.ClickSelect(world, view, Screen(46, 40), true) && selection.Selected.SequenceEqual([r1.Id, r2.Id]), "shift + click on an unselected unit adds it");
        selection.Replace([plant.Id]);
        Assert(selection.ClickSelect(world, view, Screen(40, 40), true) && selection.Selected.SequenceEqual([r1.Id]), "a selection holding a building is never extended");
        Assert(selection.BoxSelect(world, view, Box(110, 30, 125, 50), false) && selection.Selected.SequenceEqual([e1.Id]) && !selection.IsCommandable(world), "exactly one enemy in a box: info selection");
        Assert(!selection.BoxSelect(world, view, Box(110, 30, 140, 50), false) && selection.Selected.SequenceEqual([e1.Id]), "two enemies: nothing happens");
        Assert(selection.ClickSelect(world, view, Screen(46, 40), true) && selection.Selected.SequenceEqual([r2.Id]), "shift never extends an info selection");
        Assert(selection.BoxSelect(world, view, Box(30, 30, 125, 50), false) && selection.Selected.SequenceEqual([r1.Id, r2.Id]), "own units win over enemies");
        selection.Replace([e2.Id]);
        arena.System(sequence => new KillCommand(PlayerId.System, sequence, world.Tick, e2.Id));
        arena.Step(3);
        selection.Prune(world);
        Assert(selection.IsEmpty, "a dead object leaves the selection on pruning");
    }

    private static void SelectionMatching()
    {
        var arena = new Arena();
        arena.Spawn("Ranger", Me, 40, 40);
        arena.Spawn("Ranger", Me, 80, 40);
        arena.Spawn("Tank", Me, 60, 60);
        arena.Spawn("Dozer", Me, 100, 40);
        arena.Spawn("Ranger", Me, 390, 390);     // off the 400 x 300 m screen
        arena.Step(2);
        var view = new TopDownView(pixelsPerMetre: 2f, width: 800f, height: 600f);
        var world = arena.World;
        var selection = new SelectionModel(Me);
        var (r1, r2, far) = (arena.Find("Ranger", Me, 40, 40), arena.Find("Ranger", Me, 80, 40), arena.Find("Ranger", Me, 390, 390));
        var (tank, dozer) = (arena.Find("Tank", Me, 60, 60), arena.Find("Dozer", Me, 100, 40));
        Assert(selection.DoubleClickSelect(world, view, Screen(40, 40), acrossMap: false, preferSelection: false) && selection.Selected.SequenceEqual([r1.Id, r2.Id]), "double click: every ranger on screen");
        Assert(selection.DoubleClickSelect(world, view, Screen(40, 40), acrossMap: true, preferSelection: false) && selection.Selected.SequenceEqual([r1.Id, r2.Id, far.Id]), "Alt: across the map");
        selection.Replace([tank.Id]);
        Assert(selection.DoubleClickSelect(world, view, Screen(40, 40), false, preferSelection: true) && selection.Selected.SequenceEqual([r1.Id, r2.Id, tank.Id]), "Shift keeps the earlier selection");
        selection.Replace([r1.Id]);
        Assert(selection.SelectMatchingUnits(world, view) == 1 && selection.Selected.SequenceEqual([r1.Id, r2.Id]), "E adds the matching units on screen first");
        Assert(selection.SelectMatchingUnits(world, view) == 1 && selection.Selected.SequenceEqual([r1.Id, r2.Id, far.Id]), "a second E grows to the map");
        selection.Replace([dozer.Id]);
        Assert(selection.SelectAll(world, view, aircraftOnly: false) == 3 && selection.Selected.SequenceEqual([r1.Id, r2.Id, tank.Id]), "select all: no dozers, screen first, a dozer in the selection is dropped");
        var limited = new SelectionModel(Me, new SelectionSettings(MaxSelectionSize: 2));
        Assert(limited.SelectAll(world, view, false) == 2 && limited.Selected.Length == 2, "MaxSelectionSize limits select all");
        Assert(selection.SelectNext(world, workers: true, forward: true) == new Vector2(100, 40) && selection.Selected.SequenceEqual([dozer.Id]), "next worker selects and centres the dozer");
        Assert(selection.SelectNext(world, workers: false, forward: true) == new Vector2(390, 390) && selection.Selected.SequenceEqual([far.Id]), "next unit walks in id order");
        Assert(selection.SelectNext(world, workers: false, forward: true) == new Vector2(40, 40) && selection.Selected.SequenceEqual([r1.Id]), "and wraps around");
    }

    private static void ControlGroups()
    {
        var arena = new Arena();
        arena.Spawn("Ranger", Me, 40, 40);
        arena.Spawn("Ranger", Me, 80, 40);
        arena.Spawn("Tank", Me, 60, 60);
        arena.Spawn("Plant", Me, 120, 120);
        arena.Step(2);
        var world = arena.World;
        var selection = new SelectionModel(Me);
        var (r1, r2, tank, plant) = (arena.Find("Ranger", Me, 40, 40), arena.Find("Ranger", Me, 80, 40), arena.Find("Tank", Me, 60, 60), arena.Find("Plant", Me, 120, 120));
        selection.Replace([r1.Id, r2.Id]);
        Assert(selection.TeamAction(world, TeamActionKind.Create, 1, 0) is null && selection.Group(1).SequenceEqual([r1.Id, r2.Id]), "Ctrl+1 stores the selection");
        selection.Replace([r2.Id, tank.Id]);
        selection.TeamAction(world, TeamActionKind.Create, 2, 10);
        Assert(selection.Group(1).SequenceEqual([r1.Id]) && selection.Group(2).SequenceEqual([r2.Id, tank.Id]), "an object belongs to one group only");
        selection.Clear();
        Assert(selection.TeamAction(world, TeamActionKind.Select, 2, 1000) is null && selection.Selected.SequenceEqual([r2.Id, tank.Id]), "2 recalls the group");
        Assert(selection.TeamAction(world, TeamActionKind.Select, 2, 1400) == new Vector2(60, 60) && selection.Selected.SequenceEqual([r2.Id, tank.Id]), "a second press within 500 ms centres on the group's last member (the tank, highest id)");
        Assert(selection.TeamAction(world, TeamActionKind.Select, 2, 2000) is null, "later presses select again");
        Assert(selection.TeamAction(world, TeamActionKind.Add, 1, 3000) is null && selection.Selected.SequenceEqual([r1.Id, r2.Id, tank.Id]), "Shift+1 adds group 1");
        Assert(selection.TeamAction(world, TeamActionKind.View, 1, 3100) == new Vector2(40, 40) && selection.Selected.Length == 3, "Alt+1 only centres");
        selection.Replace([plant.Id]);
        selection.TeamAction(world, TeamActionKind.Add, 2, 5000);
        Assert(selection.Selected.SequenceEqual([r2.Id, tank.Id]), "adding a group drops a selected building");
        arena.System(sequence => new KillCommand(PlayerId.System, sequence, world.Tick, r2.Id));
        arena.Step(3);
        Assert(selection.TeamAction(world, TeamActionKind.Select, 2, 9000) is null && selection.Selected.SequenceEqual([tank.Id]) && selection.Group(2).SequenceEqual([tank.Id]), "dead members leave the group");
    }

    // ============================================================================================ orders

    private static (Arena Arena, CommandModeController Controller, TopDownView View) OrderSetup(Action<Arena> spawn, int money = 1000)
    {
        var arena = new Arena(money);
        spawn(arena);
        arena.Step(2);
        var selection = new SelectionModel(Me);
        return (arena, new CommandModeController(Me, arena.Sink, selection), new TopDownView());
    }

    private static void ContextOrders()
    {
        var (arena, controller, view) = OrderSetup(arena =>
        {
            arena.Spawn("Ranger", Me, 40, 40);
            arena.Spawn("Ranger", Me, 46, 40);
            arena.Spawn("Dozer", Me, 60, 60);
            arena.Spawn("Hauler", Me, 30, 80);
            arena.Spawn("Bunker", Me, 40, 110);
            arena.Spawn("Depot", Me, 90, 110);
            arena.Spawn("Barracks", Me, 150, 110);
            arena.Spawn("Plant", Me, 150, 60);
            arena.Spawn("Ranger", Enemy, 120, 40);
        });
        var world = arena.World;
        var selection = controller.Selection;
        var (r1, r2, dozer, hauler) = (arena.Find("Ranger", Me, 40, 40), arena.Find("Ranger", Me, 46, 40), arena.Find("Dozer", Me, 60, 60), arena.Find("Hauler", Me, 30, 80));
        var (bunker, depot, barracks, plant) = (arena.Find("Bunker", Me, 40, 110), arena.Find("Depot", Me, 90, 110), arena.Find("Barracks", Me, 150, 110), arena.Find("Plant", Me, 150, 60));
        var enemy = arena.Find("Ranger", Enemy, 120, 40);
        List<SimCommand> Issued(Action act)
        {
            var before = arena.Sink.Issued.Count;
            act();
            return arena.Sink.Issued.Skip(before).ToList();
        }

        selection.Replace([r1.Id, r2.Id]);
        Assert(controller.Hover(world, view, Screen(70.5f, 20.25f)) is { Action: ContextAction.Move, Cursor: CursorKind.Move }, "ground under two rangers: move");
        var move = Issued(() => controller.IssueContext(world, view, Screen(70.5f, 20.25f)));
        Assert(move.SequenceEqual<SimCommand>([
            new SetMoveTargetCommand(Me, 1, world.Tick, r1.Id, Fixed.FromDecimal(70.5m), Fixed.FromDecimal(20.25m)),
            new SetMoveTargetCommand(Me, 2, world.Tick, r2.Id, Fixed.FromDecimal(70.5m), Fixed.FromDecimal(20.25m))]), "one move per unit, id order, exact fixed-point target");
        Assert(controller.DrainFeedback() is [{ Kind: FeedbackKind.MoveMarker, DurationFrames: 40 } marker] && marker.Position == new Vector3(70.5f, 20.25f, 0), "a move marker at the destination");

        Assert(controller.Hover(world, view, Screen(120, 40)) is { Action: ContextAction.AttackObjectAfterMoving, Cursor: CursorKind.OutRange }, "an enemy out of range: attack after moving");
        Assert(controller.IssueContext(world, view, Screen(120, 40)) && controller.Orders.TryGet(r1.Id, out var attack) && attack.Kind == StandingOrderKind.Attack && attack.Target == enemy.Id,
            "attacking sets a standing order per armed unit");
        Assert(controller.DrainFeedback() is [{ Kind: FeedbackKind.AttackMarker } attackMarker] && attackMarker.Target == enemy.Id, "an attack marker on the target");

        controller.OnMeta(world, MetaAction.BeginForceAttack);
        Assert(controller.Hover(world, view, Screen(60, 60)) is { Action: ContextAction.ForceAttackObject, Cursor: CursorKind.ForceAttackObj }, "Ctrl over an own dozer: force attack");
        Assert(controller.Hover(world, view, Screen(10, 10)) is { Action: ContextAction.ForceAttackGround, Cursor: CursorKind.ForceAttackGround }, "Ctrl over ground: force attack ground");
        Assert(!controller.IssueContext(world, view, Screen(10, 10)) && controller.UnsupportedCounts["ForceAttackGround"] == 1
               && controller.DrainFeedback() is [{ Kind: FeedbackKind.Unsupported, Detail: "ForceAttackGround" }], "the simulation has no fire-at-position command: reported");
        controller.OnMeta(world, MetaAction.EndForceAttack);
        controller.OnMeta(world, MetaAction.BeginPreferSelection);
        Assert(controller.Hover(world, view, Screen(60, 60)) is { Action: ContextAction.Select }, "Shift over an own unit selects");
        controller.OnMeta(world, MetaAction.EndPreferSelection);

        Assert(controller.Hover(world, view, Screen(40, 110)) is { Action: ContextAction.Enter, Cursor: CursorKind.EnterFriendly }, "infantry over an own bunker: enter");
        var enter = Issued(() => controller.IssueContext(world, view, Screen(40, 110)));
        Assert(enter.Count == 2 && enter.OfType<EnterContainerCommand>().Select(command => (command.Actor, command.Container)).SequenceEqual([(r1.Id, bunker.Id), (r2.Id, bunker.Id)]),
            "EnterContainerCommand for each ranger");
        Assert(!controller.Orders.TryGet(r1.Id, out _), "a new order replaces the standing attack");

        selection.Replace([hauler.Id]);
        Assert(controller.Hover(world, view, Screen(90, 110)) is { Action: ContextAction.Dock, Cursor: CursorKind.Dock }, "a hauler over its supply center: dock");
        Assert(Issued(() => controller.IssueContext(world, view, Screen(90, 110))) is [DockCommand dock] && dock.Actor == hauler.Id && dock.Dock == depot.Id, "DockCommand");

        arena.System(sequence => new DamageCommand(PlayerId.System, sequence, world.Tick, plant.Id, Fixed.FromInt(100), 0, 0));
        arena.Step();
        selection.Replace([dozer.Id]);
        Assert(controller.Hover(world, view, Screen(150, 60)) is { Action: ContextAction.Repair, Cursor: CursorKind.DoRepair }, "a dozer over a damaged structure: repair");
        Assert(Issued(() => controller.IssueContext(world, view, Screen(150, 60))) is [RepairCommand repair] && repair.Builder == dozer.Id && repair.Target == plant.Id, "RepairCommand");

        selection.Replace([barracks.Id]);
        Assert(controller.Hover(world, view, Screen(170, 90)) is { Action: ContextAction.SetRallyPoint, Cursor: CursorKind.SetRallyPoint }, "a producer and the ground: rally point");
        Assert(Issued(() => controller.IssueContext(world, view, Screen(170, 90))) is [SetRallyPointCommand rally] && rally.Producer == barracks.Id
               && rally.X == Fixed.FromInt(170) && rally.Y == Fixed.FromInt(90), "SetRallyPointCommand");
        selection.Replace([plant.Id]);
        Assert(controller.Hover(world, view, Screen(170, 20)) is { Action: ContextAction.Invalid, Cursor: CursorKind.GenericInvalid }, "a lone non-producing structure cannot move");
        Assert(Issued(() => controller.IssueContext(world, view, Screen(170, 20))).Count == 0 && controller.DrainFeedback().Any(value => value.Kind == FeedbackKind.InvalidOrder), "invalid click: feedback only");

        arena.Step();
        Assert(world.RejectionCounts.Sum() == 0, $"every issued command was accepted by the simulation ({string.Join(",", world.RejectionCounts)})");
        Assert(r1.ContainedBy == bunker.Id && r2.ContainedBy == bunker.Id && barracks.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.ProductionUpdate>().Any(), "the orders took effect");
    }

    private static void StandingOrderBehaviour()
    {
        var (arena, controller, view) = OrderSetup(arena =>
        {
            arena.Spawn("Ranger", Me, 40, 40);
            arena.Spawn("Tank", Me, 40, 60);
            arena.Spawn("Ranger", Enemy, 120, 40);
            arena.Spawn("Ranger", Enemy, 60, 120);
        });
        var world = arena.World;
        var selection = controller.Selection;
        var (ranger, tank) = (arena.Find("Ranger", Me, 40, 40), arena.Find("Tank", Me, 40, 60));
        var (target, other) = (arena.Find("Ranger", Enemy, 120, 40), arena.Find("Ranger", Enemy, 60, 120));

        selection.Replace([ranger.Id]);
        controller.IssueContext(world, view, Screen(120, 40));
        var shots = 0;
        var moves = 0;
        for (var tick = 0; tick < CnCNew.Tests.TestTime.Frames(world, 150) && world.FindEntity(target.Id) is { EffectivelyDead: false }; tick++)
        {
            var before = arena.Sink.Issued.Count;
            controller.Update(world);
            foreach (var command in arena.Sink.Issued.Skip(before))
            {
                if (command is FireWeaponCommand fire) { shots++; Assert(fire.Source == ranger.Id && fire.Target == target.Id, "fires at the ordered target"); }
                if (command is SetMoveTargetCommand or PursueCommand) moves++;
            }
            arena.Step();
        }
        Assert(moves >= 1 && shots >= 1, $"the ranger chased ({moves} moves) and fired ({shots} shots)");
        Assert(world.RejectionCounts.Sum() == 0, "no standing-order command was rejected");

        selection.Replace([tank.Id]);
        controller.OnMeta(world, MetaAction.Stop);
        Assert(arena.Sink.Issued[^1] is SetMoveTargetCommand halt && halt.Target == tank.Id && halt.X == tank.X && halt.Y == tank.Y, "stop halts the unit where it stands");
        controller.OnMeta(world, MetaAction.AttackMove);
        Assert(controller.Mode == CommandModeKind.AttackMove && controller.Hover(world, view, Screen(60, 100)).Cursor == CursorKind.AttackMove, "attack-move mode shows its cursor");
        Assert(controller.CompleteTarget(world, view, Screen(60, 100)) && controller.Orders.TryGet(tank.Id, out var attackMove) && attackMove.Kind == StandingOrderKind.AttackMove
               && controller.Mode == CommandModeKind.None, "the click completes the attack move");
        Assert(arena.Sink.Issued[^1] is AttackMoveCommand marchOrder && marchOrder.Unit == tank.Id, "the attack move is the simulation's AttackMoveCommand");
        var fired = false;
        var orderedShots = 0;
        for (var tick = 0; tick < CnCNew.Tests.TestTime.Frames(world, 120) && !fired; tick++)
        {
            var before = arena.Sink.Issued.Count;
            controller.Update(world);
            orderedShots += arena.Sink.Issued.Skip(before).OfType<FireWeaponCommand>().Count();
            arena.Step();
            fired = world.AcquiredVictimOf(tank) == other.Id;
        }
        Assert(fired && orderedShots == 0, $"an attack-moving unit engages an enemy that comes into range by itself: the simulation picks the victim, the client orders no shot (engaged {fired}, {orderedShots} ordered shots, tank at {tank.X.ToDecimal():F1}/{tank.Y.ToDecimal():F1}, tick {world.Tick})");

        selection.Replace([ranger.Id, tank.Id]);
        controller.OnMeta(world, MetaAction.BeginWaypoints);
        Assert(controller.Hover(world, view, Screen(30, 30)).Cursor == CursorKind.Waypoint, "Alt: waypoint cursor");
        var before2 = arena.Sink.Issued.Count;
        controller.IssueContext(world, view, Screen(30, 30));
        controller.IssueContext(world, view, Screen(30, 10));
        controller.OnMeta(world, MetaAction.EndWaypoints);
        Assert(arena.Sink.Issued.Skip(before2).Count() == 2 && controller.Orders.TryGet(tank.Id, out var path) && path.Waypoints is [var second] && second == new Vector2(30, 10),
            "the first waypoint is ordered at once, the second queued");
        var reached = false;
        for (var tick = 0; tick < 300 && !reached; tick++)
        {
            var before = arena.Sink.Issued.Count;
            controller.Update(world);
            reached = arena.Sink.Issued.Skip(before).OfType<SetMoveTargetCommand>().Any(command => command.Target == tank.Id && command.Y == Fixed.FromInt(10));
            arena.Step();
        }
        Assert(reached, "arriving at the first waypoint orders the second");

        var scatter = new List<SimCommand>();
        var mark = arena.Sink.Issued.Count;
        controller.Scatter(world, [ranger, tank]);
        scatter.AddRange(arena.Sink.Issued.Skip(mark));
        Assert(scatter.Count == 2 && scatter.All(command => command is SetMoveTargetCommand), "scatter moves every mobile unit");
        var tankMove = scatter.OfType<SetMoveTargetCommand>().Single(command => command.Target == tank.Id);
        var away = new Vector2((float)(tankMove.X - tank.X).ToDecimal(), (float)(tankMove.Y - tank.Y).ToDecimal());
        Assert(Math.Abs(away.Length() - 24f) < 0.01f, $"four bounding radii (4 x 6 m) away from the centroid ({away.Length()})");
    }

    // ============================================================================================ placement

    private static void PlacementRules()
    {
        var (arena, controller, view) = OrderSetup(arena =>
        {
            arena.Spawn("Dozer", Me, 40, 40);
            arena.Spawn("Plant", Me, 80, 40);
        }, money: 160);
        var world = arena.World;
        var dozer = arena.Find("Dozer", Me, 40, 40);
        PlacementIssue Check(string template, float x, float y) => SimulationQueries.CheckPlacement(world, Me, dozer, template, SimulationQueries.ToFixed(x), SimulationQueries.ToFixed(y), Fixed.Zero);
        Assert(Check("Plant", 40, 80) == PlacementIssue.None, "a free explored spot is legal");
        Assert(Check("Plant", 85, 45) == PlacementIssue.BlockedFootprint, "overlapping the plant");
        Assert(Check("Plant", 155, 155) == PlacementIssue.BlockedFootprint, "a cliff cell");
        Assert(Check("Plant", 5000, 40) == PlacementIssue.BlockedFootprint, "off the map");
        Assert(Check("Plant", 180, 180) == PlacementIssue.Shroud, "never explored");
        Assert(Check("Tower", 40, 80) == PlacementIssue.None, "a finished plant satisfies the tower prerequisite");
        Assert(Check("Ranger", 40, 80) == PlacementIssue.CannotConstruct, "units are not constructed");
        Assert(Check("Nothing", 40, 80) == PlacementIssue.UnknownTemplate, "unknown template");
        Assert(SimulationQueries.CheckPlacement(world, Me, null, "Plant", Fixed.FromInt(40), Fixed.FromInt(80), Fixed.Zero) == PlacementIssue.BuilderUnavailable, "no builder");
        arena.System(sequence => new DestroyCommand(PlayerId.System, sequence, world.Tick, arena.Find("Plant", Me, 80, 40).Id));
        arena.Step();
        Assert(Check("Tower", 40, 80) == PlacementIssue.MissingPrerequisite, "without the plant the tower lacks its prerequisite");

        var buttons = CommandButtonCatalog.Build(Fixture.Graph);
        Assert(buttons.TryGetButton("Command_ConstructPlant", out var construct), "construct button");
        controller.Selection.Replace([dozer.Id]);
        Assert(controller.Activate(world, construct, controller.Selection.OwnSelected(world)) == ActivationResult.EnteredMode && controller.Mode == CommandModeKind.Placement, "construct enters placement");
        controller.Hover(world, view, Screen(40, 80));
        Assert(controller.Ghost is { Template: "Plant", IsValid: true, Anchored: false } ghost && ghost.Position == new Vector3(40, 80, 0) && controller.Cursor == CursorKind.Build, "the ghost follows the cursor");
        controller.Hover(world, view, Screen(180, 180));
        Assert(controller.Ghost is { IsValid: false, Reason: PlacementIssue.Shroud } && controller.Cursor == CursorKind.InvalidBuild, "the ghost reports why it is invalid");
        controller.PlacementPress(world, view, Screen(40, 80));
        controller.UpdateGhost(world, view, Screen(40, 80) + new Vector2(0, -4));
        Assert(controller.Ghost is { Anchored: true, Angle: 0f }, "under 5 px the angle stays");
        controller.UpdateGhost(world, view, Screen(40, 90));
        Assert(controller.Ghost is { Anchored: true } rotated && Math.Abs(rotated.Angle - MathF.PI / 2) < 1e-5f, "dragging north rotates the ghost to 90 degrees");
        var before = arena.Sink.Issued.Count;
        Assert(controller.PlacementRelease(world, view, Screen(40, 90)) && controller.Mode == CommandModeKind.None, "release places");
        Assert(arena.Sink.Issued.Skip(before).Single() is ConstructCommand command && command.Builder == dozer.Id && command.Template == "Plant"
               && command.X == Fixed.FromInt(40) && command.Y == Fixed.FromInt(80) && command.Angle == SimulationQueries.ToFixed(MathF.PI / 2), "ConstructCommand at the anchor with the dragged angle");
        arena.Step();
        Assert(world.Entities.Any(entity => entity.Template.Name == "Plant" && entity.HasStatus(ObjectStatusBits.UnderConstruction)), "the simulation accepted and started the site");

        controller.Activate(world, construct, controller.Selection.OwnSelected(world));
        Assert(controller.Mode == CommandModeKind.None, "a busy dozer cannot start a second placement");
        var idleDozer = world.Entities.First(entity => entity.Template.Name == "Dozer");
        arena.System(sequence => new GiveMoneyCommand(PlayerId.System, sequence, world.Tick, Me, Fixed.FromInt(10)));
        arena.Step();
        Assert(SimulationQueries.CheckPlacement(world, Me, idleDozer, "Plant", Fixed.FromInt(40), Fixed.FromInt(140), Fixed.Zero) == PlacementIssue.InsufficientFunds, "70 left of a 100 cost");
    }

    private static void PlacementViewAngle()
    {
        var arena = new Arena(300);
        arena.Spawn("Dozer", Me, 40, 40);
        arena.Step(2);
        var world = arena.World;
        var dozer = arena.Find("Dozer", Me, 40, 40);
        var view = new TopDownView();
        var angle = -MathF.PI / 4;
        var controller = new CommandModeController(Me, arena.Sink, new SelectionModel(Me), new CommandSettings(PlacementViewAngle: template => template == "Plant" ? angle : 0f));
        Assert(CommandButtonCatalog.Build(Fixture.Graph).TryGetButton("Command_ConstructPlant", out var construct), "construct button");
        controller.Selection.Replace([dozer.Id]);
        controller.Activate(world, construct, controller.Selection.OwnSelected(world));
        controller.Hover(world, view, Screen(40, 80));
        Assert(controller.Ghost is { Template: "Plant", Angle: var start } && start == angle, "the ghost starts turned by the template's view angle");
        controller.PlacementPress(world, view, Screen(40, 80));
        var before = arena.Sink.Issued.Count;
        Assert(controller.PlacementRelease(world, view, Screen(40, 80)), "release places");
        Assert(arena.Sink.Issued.Skip(before).Single() is ConstructCommand command && command.Angle == SimulationQueries.ToFixed(angle), "a click without a drag places the building with that angle");
    }

    // ============================================================================================ command bar

    private static void CommandBarStates()
    {
        var (arena, controller, view) = OrderSetup(arena =>
        {
            arena.Spawn("Dozer", Me, 40, 40);
            arena.Spawn("Barracks", Me, 100, 100);
            arena.Spawn("Ranger", Me, 60, 40);
            arena.Spawn("Tank", Me, 70, 40);
        }, money: 450);
        var world = arena.World;
        var catalog = CommandButtonCatalog.Build(Fixture.Graph);
        Assert(catalog.Issues.IsEmpty && catalog.ButtonCount == 10 && catalog.SetCount == 5, $"the fixture catalog is clean ({string.Join("; ", catalog.Issues)})");
        var labels = new Dictionary<string, string> { ["CONTROLBAR:TrainRanger"] = "Train &Ranger", ["CONTROLBAR:UpgradeFlash"] = "&Flashbangs", ["CONTROLBAR:SetRallyPoint"] = "Set &Rally" };
        var bar = new CommandBarModel(catalog, label => labels.GetValueOrDefault(label));
        var selection = controller.Selection;
        var (dozer, barracks, ranger, tank) = (arena.Find("Dozer", Me, 40, 40), arena.Find("Barracks", Me, 100, 100), arena.Find("Ranger", Me, 60, 40), arena.Find("Tank", Me, 70, 40));

        selection.Replace([dozer.Id]);
        var dozerBar = bar.Build(world, selection);
        Assert(dozerBar.Context == CommandBarContext.Command && dozerBar.CommandSet == "DozerCommandSet" && dozerBar.Slots.Length == 14, "a single dozer shows its set in 14 slots");
        Assert(dozerBar[1] is { Availability: ButtonAvailability.Available, Button.Name: "Command_ConstructPlant" } plantButton && plantButton.Cost == Fixed.FromInt(100) && plantButton.BuildTicks == CnCNew.Tests.TestTime.Ticks(world, 2),
            "construct plant: available, cost 100, 2 s of build time");
        Assert(dozerBar[3] is { Availability: ButtonAvailability.Restricted, Reason: ButtonReason.MissingPrerequisite }, "construct tower: the plant is missing");
        Assert(dozerBar[4] is null && dozerBar[14] is { Button.Kind: GuiCommandKind.Stop }, "empty and stop slots");

        selection.Replace([barracks.Id]);
        var barracksBar = bar.Build(world, selection);
        Assert(barracksBar[1] is { Availability: ButtonAvailability.Restricted, Reason: ButtonReason.Underpowered }, "the POWERED barracks consumes 2 without a plant: underpowered");
        Assert(barracksBar[13] is { Availability: ButtonAvailability.Available } && barracksBar[14] is { Availability: ButtonAvailability.Available }, "rally point and sell are exempt");
        var hud = bar.Hud(world, Me);
        Assert(hud.Underpowered && hud.EnergyProduction == 0 && hud.EnergyConsumption == 2 && hud.Money == Fixed.FromInt(450), "HUD: 0 produced, 2 consumed, 450 money");
        arena.Spawn("Plant", Me, 150, 150);
        arena.Step();
        barracksBar = bar.Build(world, selection);
        Assert(barracksBar[1] is { Availability: ButtonAvailability.Available, Hotkey: 'r' } && barracksBar[13] is { Hotkey: null }, "powered again; the &R hotkey goes to the first slot only");
        Assert(barracksBar[2] is { Availability: ButtonAvailability.Available, Hotkey: 'f', Cost: var flashCost } && flashCost == Fixed.FromInt(300), "the upgrade costs 300");
        Assert(bar.Hud(world, Me) is { Underpowered: false, EnergyProduction: 5, EnergyConsumption: 2 }, "5 produced, 2 consumed");

        Assert(bar.ActivateHotkey(world, barracksBar, 'R', controller, out var trained) == HotkeyActivation.Activated && trained == ActivationResult.Issued, "R trains a ranger");
        Assert(arena.Sink.Issued[^1] is QueueProductionCommand queue && queue.Producer == barracks.Id && queue.Template == "Ranger", "QueueProductionCommand");
        arena.Step();
        Assert(bar.Activate(world, bar.Build(world, selection), 1, controller) == ActivationResult.Issued, "a second ranger (250 left)");
        arena.Step(5);
        barracksBar = bar.Build(world, selection);
        Assert(barracksBar[1] is { Availability: ButtonAvailability.Restricted, Reason: ButtonReason.QueueFull }, "two queued of MaxQueueEntries 2: queue full");
        Assert(barracksBar[2] is { Availability: ButtonAvailability.Restricted, Reason: ButtonReason.InsufficientFunds }, "50 left: the 300 upgrade is unaffordable");
        Assert(barracksBar.Queue is [var head, var next] && head.Template == "Ranger" && head.ButtonImage == "SRanger" && head.Cost == Fixed.FromInt(200)
               && head.Progress > 0f && head.Progress < 1f && next.Progress == 0f, $"queue view: head in progress, second waiting ({string.Join(", ", barracksBar.Queue)})");
        Assert(controller.CancelProduction(barracks.Id, 1), "a queue icon cancels its entry");
        arena.Step();
        Assert(world.MoneyOf(Me) == Fixed.FromInt(250) && bar.Build(world, selection).Queue.Length == 1, "the cancelled entry is refunded");

        arena.System(sequence => new GiveMoneyCommand(PlayerId.System, sequence, world.Tick, Me, Fixed.FromInt(100)));
        arena.Step();
        Assert(bar.Activate(world, bar.Build(world, selection), 2, controller) == ActivationResult.Issued && arena.Sink.Issued[^1] is PurchaseUpgradeCommand { Upgrade: "Upgrade_Flash", Target: null },
            "the player upgrade is bought");
        arena.Step();
        Assert(bar.Build(world, selection)[2] is { Availability: ButtonAvailability.CantAfford, Reason: ButtonReason.AlreadyOwned }, "an owned upgrade shows as already owned");

        selection.Replace([ranger.Id, tank.Id]);
        var multi = bar.Build(world, selection);
        Assert(multi.Context == CommandBarContext.MultiSelect && multi[11] is { Button.Kind: GuiCommandKind.AttackMove } attackMove && attackMove.Sources.SequenceEqual([ranger.Id, tank.Id])
               && multi[14] is { Button.Kind: GuiCommandKind.Stop } && multi[12] is null && multi[13] is null, "multi-select keeps the common OK_FOR_MULTI_SELECT buttons of the same slot");
        Assert(bar.Activate(world, multi, 11, controller) == ActivationResult.EnteredMode && controller.Mode == CommandModeKind.AttackMove, "attack move for both");
        controller.Cancel();
        selection.Replace([dozer.Id, ranger.Id]);
        Assert(bar.Build(world, selection) is { Context: CommandBarContext.MultiSelect } mixed && mixed[14] is { Button.Kind: GuiCommandKind.Stop } && mixed[1] is null, "construct buttons are not multi-select");
        arena.Spawn("Ranger", Enemy, 80, 60);
        arena.Step();
        selection.Replace([world.Entities.First(entity => entity.Owner == Enemy).Id]);
        Assert(bar.Build(world, selection).Context == CommandBarContext.Foreign, "a foreign object shows no buttons");
    }

    private static void CameraIntents()
    {
        var hotkeys = HotkeyMap.Parse(CommandMapText).WithEngineDefaults();
        var camera = new CameraInputController();
        var state = new InputState();
        var viewport = new Vector2(800, 600);
        state.Apply(new MouseMoveEvent(0, new Vector2(400, 300)));
        Assert(camera.Update(state, hotkeys, viewport, CnCNew.Tests.TestTime.FrameSecondsF).IsIdle, "a centred cursor and no keys: idle");
        state.Apply(new KeyEvent(1, KeyCode.Up, true));
        var up = camera.Update(state, hotkeys, viewport, 1f);
        Assert(up.Scroll == new Vector2(0, 2.0f * 0.5f), "the up arrow scrolls forward: vertical factor 2.0 x scroll option 0.5 per second");
        state.Apply(new KeyEvent(2, KeyCode.LeftControl, true));
        Assert(camera.Update(state, hotkeys, viewport, 1f).Scroll == Vector2.Zero, "Ctrl+Up is not a scroll (it selects the next worker)");
        state.Apply(new KeyEvent(3, KeyCode.LeftControl, false));
        state.Apply(new KeyEvent(4, KeyCode.Up, false));
        state.Apply(new MouseMoveEvent(5, new Vector2(1, 300)));
        Assert(camera.Update(state, hotkeys, viewport, 1f).Scroll == new Vector2(-1.6f * 0.5f, 0), "the cursor within 3 px of the left edge scrolls left: horizontal factor 1.6 x scroll option 0.5");
        state.Apply(new MouseMoveEvent(6, new Vector2(3, 300)));
        Assert(camera.Update(state, hotkeys, viewport, 1f).Scroll == Vector2.Zero, "3 px from the edge is outside the band");
        var noEdge = new CameraInputController(new CameraSettings(EdgeScrollEnabled: false));
        state.Apply(new MouseMoveEvent(7, new Vector2(0, 0)));
        Assert(noEdge.Update(state, hotkeys, viewport, 1f).Scroll == Vector2.Zero, "edge scrolling can be switched off");
        state.Apply(new MouseMoveEvent(8, new Vector2(400, 300)));
        state.Apply(new MouseButtonEvent(9, MouseButton.Right, true, new Vector2(400, 300)));
        state.Apply(new MouseMoveEvent(10, new Vector2(600, 150)));
        var drag = camera.Update(state, hotkeys, viewport, 1f);
        Assert(drag.Scroll == new Vector2(0.5f * 1.6f, 0.5f * 2.0f), "right drag: half a half-screen right and up");
        state.Apply(new MouseButtonEvent(11, MouseButton.Right, false, new Vector2(600, 150)));
        state.Apply(new MouseMoveEvent(12, new Vector2(400, 300)));
        camera.OnWheel(2);
        camera.OnMeta(MetaAction.BeginCameraRotateLeft);
        var spin = camera.Update(state, hotkeys, viewport, CnCNew.Tests.TestTime.FrameSecondsF);
        Assert(spin.ZoomDelta == -20f && Math.Abs(spin.RotateDelta - 0.1f) < 1e-6f, "two wheel detents zoom in 20; rotate-left turns 0.1 rad per frame");
        camera.OnMeta(MetaAction.EndCameraRotateLeft);
        camera.OnGesture(new PointerGesture(GestureKind.Click, MouseButton.Middle, new Vector2(10, 10), new Vector2(13, 13), KeyModifiers.None, 0));
        camera.RequestCenter(new Vector2(40, 40));
        var reset = camera.Update(state, hotkeys, viewport, CnCNew.Tests.TestTime.FrameSecondsF);
        Assert(reset.Reset && reset.CenterOn == new Vector2(40, 40) && reset.RotateDelta == 0, "a middle click resets, a centre request is passed on");
        Assert(camera.Update(state, hotkeys, viewport, CnCNew.Tests.TestTime.FrameSecondsF).IsIdle, "one-shot requests are consumed");
        camera.OnGesture(new PointerGesture(GestureKind.DragStarted, MouseButton.Middle, new Vector2(100, 100), new Vector2(130, 100), KeyModifiers.None, 0));
        camera.OnGesture(new PointerGesture(GestureKind.DragMoved, MouseButton.Middle, new Vector2(100, 100), new Vector2(150, 100), KeyModifiers.None, 0));
        Assert(Math.Abs(camera.Update(state, hotkeys, viewport, 0).RotateDelta - 0.5f) < 1e-5f, "a 50 px middle drag rotates 0.5 rad");
    }

    // ============================================================================================ acceptance

    private static void MouseSchemes()
    {
        Assert(new GameInputSettings().Scheme == MouseScheme.RightClickCommands, "the default: left selects, right orders");
        foreach (var scheme in new MouseScheme?[] { null, MouseScheme.RightClickCommands, MouseScheme.LeftClickCommands })
        {
            var arena = new Arena(money: 500);
            arena.Spawn("Ranger", Me, 40, 40);
            arena.Step(2);
            var world = arena.World;
            var view = new TopDownView();
            var input = new GameInputController(Me, arena.Sink, HotkeyMap.Parse(CommandMapText).WithEngineDefaults(), CommandButtonCatalog.Build(Fixture.Graph),
                _ => null, scheme is { } chosen ? new GameInputSettings(chosen) : null);
            var ranger = arena.Find("Ranger", Me, 40, 40);
            long time = 10_000;
            void Click(MouseButton button, Vector2 at)
            {
                input.Process(new MouseMoveEvent(time += 16, at), world, view);
                input.Process(new MouseButtonEvent(time += 16, button, true, at), world, view);
                input.Process(new MouseButtonEvent(time += 50, button, false, at), world, view);
                time += 1000;
            }
            var alternate = scheme != MouseScheme.LeftClickCommands;
            Click(MouseButton.Left, Screen(40, 40));
            Assert(input.Selection.Selected.SequenceEqual([ranger.Id]), $"{scheme}: a left click on the ranger selects it");
            Click(MouseButton.Right, Screen(100, 20));
            Assert(arena.Sink.Issued.Count == (alternate ? 1 : 0), $"{scheme}: a right click on the ground orders only in the alternate setup ({arena.Sink.Issued.Count})");
            Assert(input.Selection.Selected.IsEmpty == !alternate, $"{scheme}: a right click deselects only in the original's setup");
            if (!alternate) Click(MouseButton.Left, Screen(40, 40));
            Click(MouseButton.Left, Screen(120, 100));
            Assert(arena.Sink.Issued.Count == 1 && arena.Sink.Issued[0] is SetMoveTargetCommand, $"{scheme}: a left click on the ground orders only in the original's setup ({arena.Sink.Issued.Count})");
            Assert(input.Selection.Selected.IsEmpty == alternate, $"{scheme}: a left click on empty ground deselects in the alternate setup");
        }
    }

    private static void AcceptanceSequence()
    {
        var arena = new Arena(money: 500);
        arena.Spawn("Ranger", Me, 40, 40);
        arena.Spawn("Ranger", Me, 46, 40);
        arena.Spawn("Dozer", Me, 60, 70);
        arena.Spawn("Plant", Me, 150, 40);
        arena.Step(2);
        var world = arena.World;
        var view = new TopDownView();
        var labels = new Dictionary<string, string> { ["CONTROLBAR:ConstructPlant"] = "Construct &Plant" };
        var input = new GameInputController(Me, arena.Sink, HotkeyMap.Parse(CommandMapText).WithEngineDefaults(), CommandButtonCatalog.Build(Fixture.Graph),
            label => labels.GetValueOrDefault(label), new GameInputSettings(MouseScheme.RightClickCommands));
        var (r1, r2, dozer) = (arena.Find("Ranger", Me, 40, 40), arena.Find("Ranger", Me, 46, 40), arena.Find("Dozer", Me, 60, 70));
        long time = 10_000;
        void Send(InputEvent inputEvent) => input.Process(inputEvent, world, view);
        void Click(MouseButton button, Vector2 at)
        {
            Send(new MouseMoveEvent(time += 16, at));
            Send(new MouseButtonEvent(time += 16, button, true, at));
            Send(new MouseButtonEvent(time += 50, button, false, at));
            time += 1000;
        }
        void Key(KeyCode key, KeyCode? modifier = null)
        {
            if (modifier is { } held) Send(new KeyEvent(time += 16, held, true));
            Send(new KeyEvent(time += 16, key, true));
            Send(new KeyEvent(time += 16, key, false));
            if (modifier is { } released) Send(new KeyEvent(time += 16, released, false));
            time += 1000;
        }
        void Tick()
        {
            input.Tick(world);
            world.Step();
        }

        // 1. Box-select both rangers (drag from the south-west to the north-east of them).
        Send(new MouseMoveEvent(time, Screen(30, 30)));
        Send(new MouseButtonEvent(time += 16, MouseButton.Left, true, Screen(30, 30)));
        Send(new MouseMoveEvent(time += 16, Screen(50, 50)));
        Send(new MouseButtonEvent(time += 16, MouseButton.Left, false, Screen(50, 50)));
        Assert(input.Selection.Selected.SequenceEqual([r1.Id, r2.Id]), "the box selects both rangers");
        // 2. Ctrl+1 stores them, then a right click moves them.
        Key(KeyCode.D1, KeyCode.LeftControl);
        Assert(input.Group(1).SequenceEqual([r1.Id, r2.Id]), "Ctrl+1 stores them");
        Click(MouseButton.Right, Screen(100, 20));
        Tick();
        // 3. Select the dozer, press P (the &P of its construct button), place the plant with a click.
        Click(MouseButton.Left, Screen(60, 70));
        Assert(input.Selection.Selected.SequenceEqual([dozer.Id]), "a click selects the dozer");
        Key(KeyCode.P);
        Assert(input.Commands.Mode == CommandModeKind.Placement, "P enters placement");
        Send(new MouseMoveEvent(time += 16, Screen(60, 110)));
        Assert(input.Commands.Ghost is { IsValid: true, Template: "Plant" } && input.Hint.Cursor == CursorKind.Build, "a valid ghost under the cursor");
        Click(MouseButton.Left, Screen(60, 110));
        Assert(input.Commands.Mode == CommandModeKind.None, "the click placed the plant");
        Tick();
        // 4. Recall group 1 and send it on.
        Key(KeyCode.D1);
        Assert(input.Selection.Selected.SequenceEqual([r1.Id, r2.Id]), "1 recalls the rangers");
        Click(MouseButton.Right, Screen(20, 120));
        for (var tick = 0; tick < 5; tick++) Tick();

        var mine = arena.Sink.Issued;
        Assert(mine.SequenceEqual<SimCommand>([
            new SetMoveTargetCommand(Me, 1, 2, r1.Id, Fixed.FromInt(100), Fixed.FromInt(20)),
            new SetMoveTargetCommand(Me, 2, 2, r2.Id, Fixed.FromInt(100), Fixed.FromInt(20)),
            new ConstructCommand(Me, 3, 3, dozer.Id, "Plant", Fixed.FromInt(60), Fixed.FromInt(110), Fixed.Zero),
            new SetMoveTargetCommand(Me, 4, 4, r1.Id, Fixed.FromInt(20), Fixed.FromInt(120)),
            new SetMoveTargetCommand(Me, 5, 4, r2.Id, Fixed.FromInt(20), Fixed.FromInt(120))]),
            $"the exact command stream: {string.Join(" | ", mine)}");
        Assert(world.RejectionCounts.Sum() == 0, "every command was accepted");
        Assert(world.Entities.Count(entity => entity.Template.Name == "Plant") == 2, "the site stands");

        var replay = arena.Replay(world.Tick);
        var bytes = ReplayFormat.Write(replay);
        var rerun = ReplayRunner.Run(ReplayFormat.Read(bytes), Fixture.Templates, Fixture.Fingerprint, terrain: arena.Terrain);
        Assert(rerun.RejectedCommands == 0 && rerun.FinalHash == world.StateHash, "the recorded commands replay to the same state hash");
    }

    // ============================================================================================ real data

    private static void RealData(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);

        var text = Encoding.Latin1.GetString(ReadAll(content, "Data/English/CommandMap.ini"));
        var parsed = HotkeyMap.Parse(text);
        var fromGraph = HotkeyMap.FromGraph(graph);
        Assert(parsed.Issues.IsEmpty, $"the retail CommandMap.ini parses without issues: {string.Join("; ", parsed.Issues)}");
        Assert(parsed.Compatibility.All(note => note.Kind == HotkeyIssueKind.FieldWithoutEquals), "the only compatibility case is a field without '='");
        var differing = parsed.Entries.Zip(fromGraph.Entries).Where(pair => pair.First != pair.Second).ToList();
        Console.WriteLine($"       CommandMap graph vs text: {fromGraph.Entries.Length}/{parsed.Entries.Length} entries, {differing.Count} differ, {parsed.Compatibility.Length} fields without '=' "
                          + $"({string.Join(", ", parsed.Compatibility.Select(note => note.Subject))})");
        foreach (var (textEntry, graphEntry) in differing.Take(3)) Console.WriteLine($"         text {textEntry} / graph {graphEntry}");
        Assert(fromGraph.Issues.IsEmpty && fromGraph.Entries.Length == parsed.Entries.Length
               && differing.All(pair => pair.First with { DescriptionLabel = "" } == pair.Second with { DescriptionLabel = "" }
                                        && parsed.Compatibility.Any(note => note.Subject == MetaActionNames.NameOf(pair.First.Action))),
            "the compiled graph carries the same bindings; it only lacks the descriptions written without '='");
        Assert(parsed.TryGetEntry(MetaAction.Stop, out var stop) && stop.Binding == new KeyBinding(KeyCode.S, KeyModifiers.None), "STOP = S");
        Assert(parsed.TryGetEntry(MetaAction.BeginForceAttack, out var force) && force.Binding == new KeyBinding(KeyCode.None, KeyModifiers.Control), "force attack = Ctrl alone");
        Assert(parsed.TryGetEntry(MetaAction.SelectTeam5, out var team) && team.Binding == new KeyBinding(KeyCode.D5, KeyModifiers.None), "SELECT_TEAM5 = 5");
        Console.WriteLine($"       CommandMap: {parsed.Entries.Length} entries, {parsed.Conflicts.Length} conflicts{(parsed.Conflicts.IsEmpty ? "" : ": " + string.Join(", ", parsed.Conflicts.Select(c => $"{c.Binding}={string.Join("+", c.Actions)}")))}");

        var buttons = CommandButtonCatalog.Build(graph);
        Console.WriteLine($"       command buttons: {buttons.ButtonCount} buttons ({buttons.ButtonsWithoutCommand} without Command), {buttons.SetCount} sets, {buttons.SlotsBeyondGrid} set entries beyond slot 14, {buttons.Issues.Length} issues");
        foreach (var issue in buttons.Issues.Take(10)) Console.WriteLine($"         issue {issue.Definition}: {issue.Detail}");
        Assert(buttons.Issues.IsEmpty, "every retail command button and set is understood");
        Assert(buttons.TryGetButton("Command_InitiateBattlePlanBombardment", out var plan1) && plan1.SpecialPowerOption == 1
               && buttons.TryGetButton("Command_InitiateBattlePlanHoldTheLine", out var plan2) && plan2.SpecialPowerOption == 2
               && buttons.TryGetButton("Command_InitiateBattlePlanSearchAndDestroy", out var plan3) && plan3.SpecialPowerOption == 3
               && buttons.TryGetButton("Command_ConstructAmericaDozer", out var dozerButton) && dozerButton.SpecialPowerOption == 0,
            "the strategy center's three battle plan buttons carry OPTION_ONE/TWO/THREE as the command's option");

        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, CnCNew.Tests.TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(Me, "America", "FactionAmerica"), new PlayerConfiguration(Enemy, "China", "FactionChina"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), CnCNew.Tests.TestRate.Default, 3, players, "input-real", fingerprint), catalog);
        var system = 0UL;
        var names = new[] { "AmericaCommandCenter", "AmericaBarracks", "AmericaVehicleDozer", "AmericaTankCrusader" };
        for (var index = 0; index < names.Length; index++)
        {
            Assert(catalog.TryGet(names[index], out var template), $"{names[index]} exists");
            Assert(template.IsInstantiable, $"{names[index]} is instantiable (missing: {string.Join(", ", template.MissingModules)})");
            world.Submit(new SpawnCommand(PlayerId.System, ++system, 0, names[index], Me, Fixed.FromInt(100 + 80 * index), Fixed.FromInt(100), Fixed.Zero, Fixed.Zero));
        }
        world.Submit(new GiveMoneyCommand(PlayerId.System, ++system, 0, Me, Fixed.FromInt(10_000)));
        world.Step();
        world.Step();
        var bar = new CommandBarModel(buttons);
        var selection = new SelectionModel(Me);
        CommandBarView BarOf(string name)
        {
            selection.Replace([world.Entities.First(entity => entity.Template.Name == name).Id]);
            var view = bar.Build(world, selection);
            Console.WriteLine($"       {name} ({view.CommandSet}): " + string.Join(" ", view.Slots.Select((slot, index) => slot is null ? null : $"{index + 1}={slot.Button.Name}:{slot.Availability}{(slot.Reason == ButtonReason.None ? "" : "/" + slot.Reason)}").OfType<string>()));
            return view;
        }
        var center = BarOf("AmericaCommandCenter");
        Assert(center.Context == CommandBarContext.Command && center.CommandSet == "AmericaCommandCenterCommandSet", "command center set");
        Assert(center[1] is { Button.Name: "Command_ConstructAmericaDozer", Button.Kind: GuiCommandKind.UnitBuild }, "slot 1 builds the dozer");
        Assert(center[13] is { Button.Kind: GuiCommandKind.SetRallyPoint, Availability: ButtonAvailability.Available } && center[14] is { Button.Kind: GuiCommandKind.Sell, Availability: ButtonAvailability.Available },
            "rally point and sell");
        Assert(center[2] is null, "the gunship needs its general's science: hidden (NEED_SPECIAL_POWER_SCIENCE)");
        var dozer = BarOf("AmericaVehicleDozer");
        Assert(dozer[1] is { Button.Name: "Command_ConstructAmericaPowerPlant", Availability: ButtonAvailability.Available }, "the dozer can build a power plant");
        Assert(dozer[3] is { Button.Name: "Command_ConstructAmericaBarracks" }, "slot 3 is the barracks");
        Assert(dozer.Slots.Any(slot => slot is { Reason: ButtonReason.MissingPrerequisite }), "some structures wait for prerequisites");
        var crusader = BarOf("AmericaTankCrusader");
        Assert(crusader[11] is { Button.Kind: GuiCommandKind.AttackMove, Availability: ButtonAvailability.Available } && crusader[13] is { Button.Kind: GuiCommandKind.Guard }
               && crusader[14] is { Button.Kind: GuiCommandKind.Stop }, "attack move, guard, stop");
        var barracks = BarOf("AmericaBarracks");
        Assert(barracks[1] is { Button.Name: "Command_ConstructAmericaInfantryRanger" } ranger && ranger.Availability is ButtonAvailability.Available or ButtonAvailability.Restricted,
            "slot 1 trains rangers");
        Assert(barracks[13] is { Button.Kind: GuiCommandKind.SetRallyPoint } && barracks[14] is { Button.Kind: GuiCommandKind.Sell }, "rally point and sell");
        var hud = bar.Hud(world, Me);
        Console.WriteLine($"       HUD: money {hud.Money}, power {hud.EnergyProduction}/{hud.EnergyConsumption}, science points {hud.SciencePurchasePoints}, {hud.SpecialPowers.Length} special powers");
        Assert(hud.Money == Fixed.FromInt(10_000) + (catalog.Factions.TryGet("FactionAmerica", out var america) ? america.StartMoney : Fixed.Zero), "HUD money");
    }

    private static byte[] ReadAll(ContentFileSystem content, string path)
    {
        using var stream = content.OpenRead(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class RealSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }

    // ============================================================================================ helpers

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class InputRun
    {
        private int _failed;
        private int _passed;

        public void Test(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine($"  ok   {name}");
            }
            catch (Exception exception)
            {
                _failed++;
                Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
                Console.Error.WriteLine(exception.StackTrace);
            }
        }

        public int Finish()
        {
            Console.WriteLine($"  input: {_passed} passed, {_failed} failed");
            return _failed;
        }
    }
}

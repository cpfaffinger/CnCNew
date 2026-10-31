using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-20: the bridges of a map (<c>TerrainLogic.cpp</c>, <c>AIPathfind.cpp</c> <c>PathfindLayer</c>, <c>BridgeBehavior.cpp</c>). A river of water runs north and south through the middle of a map of
/// 60 by 30 cells and only a bridge crosses it. Everything here failed before, when a bridge was a pair of map points nobody read.
/// </summary>
static partial class Tests
{
    private const int RiverWest = 250;
    private const int RiverEast = 350;
    private const int BridgeY = 150;

    private const string BridgeIni = """
        Locomotor BridgeWalk
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 30.0
          Acceleration = 300.0
          Braking = 300.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Locomotor BridgeBoat
          Surfaces = WATER
          TurnRate = 360.0
          Speed = 30.0
          Acceleration = 300.0
          Braking = 300.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object Crosser
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL BridgeWalk
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Boat
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL BridgeBoat
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object BridgeDozer
          KindOf = SELECTABLE VEHICLE DOZER
          Locomotor = SET_NORMAL BridgeWalk
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 50%
            BoredTime = 500000
            BoredRange = 10
          End
        End
        Weapon DeckGun
          PrimaryDamage = 50
          PrimaryDamageRadius = 10.0
          AttackRange = 500.0
          WeaponSpeed = 300
          ProjectileObject = DeckShell
          DelayBetweenShots = 0
        End
        Object DeckShell
          KindOf = PROJECTILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DumbProjectileBehavior Dumb01
            FirstHeight = 60
            SecondHeight = 60
            FirstPercentIndent = 25%
            SecondPercentIndent = 75%
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object DeckArtillery
          KindOf = VEHICLE SELECTABLE
          VisionRange = 500.0
          ShroudClearingRange = 500.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY DeckGun
          End
        End
        Object DeckDrone
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL BridgeWalk
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlavedUpdate Slaved01
            GuardMaxRange = 60
            StayOnSameLayerAsMaster = Yes
          End
        End
        Object GenericBridge
          KindOf = STRUCTURE IMMOBILE BRIDGE
          Geometry = BOX
          GeometryMajorRadius = 1.0
          GeometryMinorRadius = 1.0
          GeometryHeight = 1.0
          GeometryIsSmall = No
          Body = ImmortalBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = BridgeBehavior Bridge01
            LateralScaffoldSpeed = 100.0
            VerticalScaffoldSpeed = 70.0
          End
        End
        Object LandmarkSpan
          IsBridge = Yes
          KindOf = STRUCTURE IMMOBILE BRIDGE LANDMARK_BRIDGE
          Geometry = BOX
          GeometryMajorRadius = 70.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 5.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = BridgeBehavior Bridge01
            LateralScaffoldSpeed = 300.0
            VerticalScaffoldSpeed = 300.0
          End
          Behavior = KeepObjectDie Keep01
          End
        End
        Object SpanTower
          KindOf = UNATTACKABLE STRUCTURE IMMOBILE BRIDGE_TOWER
          Geometry = BOX
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 30.0
          GeometryIsSmall = No
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = BridgeTowerBehavior Tower01
          End
          Behavior = KeepObjectDie Keep01
          End
        End
        Object SpanScaffold
          KindOf = IMMOBILE
          Geometry = BOX
          GeometryMajorRadius = 25.0
          GeometryMinorRadius = 15.0
          GeometryHeight = 6.0
          Body = ImmortalBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = BridgeScaffoldBehavior Scaffold01
          End
        End
        Bridge SampleSpan
          BridgeScale = 1.0
          RadarColor = R:192 G:192 B:192
          BridgeModelName = SampleModel
          Texture = Sample.tga
          ScaffoldObjectName = SpanScaffold
          ScaffoldSupportObjectName = SpanScaffold
        End
        Bridge NoModelSpan
          BridgeModelName = Missing
        End
        Bridge LandmarkSpan
          RadarColor = R:192 G:192 B:192
          TowerObjectNameFromLeft = SpanTower
          TowerObjectNameFromRight = SpanTower
          TowerObjectNameToLeft = SpanTower
          TowerObjectNameToRight = SpanTower
          ScaffoldObjectName = SpanScaffold
          ScaffoldSupportObjectName = SpanScaffold
        End
        """;

    private static readonly ModelGeometry BridgeGeometry = new([], [], [], [new KeyValuePair<string, BridgeDeck>("SampleSpan", new BridgeDeck(Fixed.FromInt(-20), Fixed.FromInt(20)))]);

    private static SyntheticFixture BridgeFixture(SimulationRate rate)
        => new(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Bridges.ini"] = BridgeIni }, geometry: BridgeGeometry);

    /// <summary>Banks of raw height 16 (10 units), a river bed of 0 between them and water over it, level 5.</summary>
    private static MapData RiverMap(params MapObject[] objects)
    {
        var map = SyntheticMaps.Build(60, 30, 1, (x, _) => x > RiverWest / 10 && x <= RiverEast / 10 ? 0 : 16,
            triggers: [SyntheticMaps.Water("River", 5, (RiverWest, -20), (RiverEast, -20), (RiverEast, 320), (RiverWest, 320))]);
        return map with { Objects = [..map.Objects, ..objects] };
    }

    private static MapObject BridgePoint(string template, decimal x, decimal y, bool first, string scriptName = "")
        => new(template, x, y, 0m, 0m, first ? MapObject.FlagBridgePoint1 : MapObject.FlagBridgePoint2, scriptName.Length > 0 ? new MapProperties([Scr.Text("objectName", scriptName)]) : MapProperties.Empty);

    private static MapObject[] SampleSpan() => [BridgePoint("SampleSpan", RiverWest - 20, BridgeY, true, "Crossing"), BridgePoint("SampleSpan", RiverEast + 20, BridgeY, false)];

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, TerrainMap Terrain) BridgeWorld(SimulationRate rate, MapData map)
    {
        var fixture = BridgeFixture(rate);
        var terrain = TerrainMap.Build(map, "river");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        return (world, new Feeder(world), fixture, terrain);
    }

    private static readonly SimulationRate[] BridgeRates = [SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120)];

    private static Entity BridgeUnit(SimulationWorld world, Feeder feeder, string template, int x, int y)
    {
        Check.That(feeder.Spawn(1, template, owner: 1, x: x, y: y).Accepted, $"{template} spawns.");
        world.Step();
        // An order is given at the start of a frame of the original, at every rate.
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        return world.Entities.OrderBy(value => value.Id.Value).Last(value => value.Template.Name == template);
    }

    /// <summary>Steps until the unit, whose order was just given, has come to rest.</summary>
    private static void UntilArrived(SimulationWorld world, Entity unit)
    {
        world.Step();
        for (var step = 0; step < world.Frames(900) && unit.HasMoveTarget; step++) world.Step();
    }

    /// <summary><c>W3DBridge::getBridgeInfo</c>, <c>Bridge::Bridge</c>: the corners lie half the model's width beside the line of the points, the deck floats a quarter unit over the ground at its ends, and a <c>GenericBridge</c> stands in its middle.</summary>
    public static void BridgesAreBuiltFromTheMapPoints()
    {
        var rate = TestRate.Default;
        var (world, _, _, terrain) = BridgeWorld(rate, RiverMap(SampleSpan()));
        Check.That(terrain.BridgeSpans.Length == 1 && world.Bridges.Count == 1, "One pair of points is one bridge.");
        var bridge = world.Bridges[0];
        Check.That(bridge.From == new DeckPoint(Fixed.FromInt(230), Fixed.FromInt(BridgeY), Fixed.FromDecimal(10.25m)), $"The deck begins a quarter unit over the bank ({bridge.From}).");
        Check.That(bridge.FromLeft.Y == Fixed.FromInt(170) && bridge.FromRight.Y == Fixed.FromInt(130) && bridge.ToLeft.X == Fixed.FromInt(370) && bridge.Width == Fixed.FromInt(40), "The corners lie half the model's width beside the line.");
        Check.That(bridge.Layer == 1, "The first bridge has the first layer.");
        var genericBridge = world.FindEntity(bridge.ObjectId)!;
        Check.That(genericBridge.Template.Name == "GenericBridge" && genericBridge.X == Fixed.FromInt(300) && genericBridge.Y == Fixed.FromInt(BridgeY) && genericBridge.Z == Fixed.FromDecimal(10.25m) && genericBridge.Angle.Abs() < Fixed.FromDecimal(0.0001m),
            $"A GenericBridge object stands in the middle of the deck, looking from the 'from' side to the 'to' side ({genericBridge.Template.Name}, {genericBridge.X}, {genericBridge.Y}, {genericBridge.Z}, {genericBridge.Angle}).");
        Check.That(bridge.IsPointOnBridge(Fixed.FromInt(300), Fixed.FromInt(165)) && !bridge.IsPointOnBridge(Fixed.FromInt(300), Fixed.FromInt(171)) && !bridge.IsPointOnBridge(Fixed.FromInt(229), Fixed.FromInt(BridgeY)), "The deck is the rectangle.");
        var (first, second) = bridge.AttackPoints();
        Check.That(first.X == Fixed.FromInt(250) && second.X == Fixed.FromInt(350), "The attack points lie half the width inside the ends.");

        // A sloping bridge has a sloping deck, and the deck is the plane through its corners.
        var slope = TerrainBridge.FromSpan("X", string.Empty, 0, new DeckPoint(Fixed.Zero, Fixed.Zero, Fixed.FromInt(10)), new DeckPoint(Fixed.FromInt(100), Fixed.Zero, Fixed.FromInt(30)), Fixed.FromInt(-5), Fixed.FromInt(5), Fixed.One);
        Check.That(slope.HeightAt(Fixed.FromInt(50), Fixed.FromInt(3)) == Fixed.FromInt(20), $"The height of the plane ({slope.HeightAt(Fixed.FromInt(50), Fixed.FromInt(3))}).");

        // loadBridges: a first point without its second is no bridge, and neither is a block without a model.
        var (lone, _, _, loneTerrain) = BridgeWorld(rate, RiverMap(BridgePoint("SampleSpan", 230m, BridgeY, true), new MapObject("Crosser", 10m, 10m, 0m, 0m, 0, MapProperties.Empty), BridgePoint("SampleSpan", 370m, BridgeY, false)));
        Check.That(loneTerrain.BridgeSpans.IsEmpty && lone.Bridges.Count == 0, "A pair must be neighbours in the file.");
        var (noModel, _, _, _) = BridgeWorld(rate, RiverMap(BridgePoint("NoModelSpan", 230m, BridgeY, true), BridgePoint("NoModelSpan", 370m, BridgeY, false)));
        Check.That(noModel.Bridges.Count == 0, "A block without a model of the pristine state makes no bridge.");

        // A world without a bridge keeps its serialized form.
        var (plain, _, plainFixture, plainTerrain) = BridgeWorld(rate, RiverMap());
        Check.That(!System.Text.Encoding.UTF8.GetString(WorldSnapshot.Write(plain)).Contains("bridge", StringComparison.OrdinalIgnoreCase) && plain.Entities.Count == 0, "A map without a bridge has nothing of it in its state.");
        Check.That(WorldSnapshot.Restore(WorldSnapshot.Write(plain), plainFixture.Templates, plainFixture.Fingerprint, plainTerrain).StateHash == plain.StateHash, "And comes back the same.");
    }

    /// <summary><c>PathfindLayer::classifyLayerMapCell</c>: the deck's cells are clear, its sides closed, its ends join the ground; the ground stays where it was.</summary>
    public static void BridgeLayerCellsAndPaths()
    {
        var (world, _, _, terrain) = BridgeWorld(TestRate.Default, RiverMap(SampleSpan()));
        var grid = world.Navigation!;
        Check.That(grid.IsDeckCellClear(1, 30, 15) && grid.IsDeckCellClear(1, 25, 14) && !grid.IsDeckCellClear(1, 30, 18) && !grid.IsDeckCellClear(1, 30, 12), "The deck is clear, its sides are not.");
        Check.That(grid.ConnectLayerAt(22, 15) == 1 && grid.ConnectLayerAt(37, 15) == 1 && grid.ConnectLayerAt(30, 15) == 0, $"The ground joins the deck half a cell beyond its ends ({grid.ConnectLayerAt(22, 15)}, {grid.ConnectLayerAt(37, 15)}).");
        Check.That(!grid.IsPassable(23, 15, LocomotorSurfaces.Ground) && !grid.IsPassable(26, 15, LocomotorSurfaces.Ground), "The bank under a deck too low to walk under is closed, the water is water.");

        var (fromX, fromY, toX, toY) = (Fixed.FromInt(100), Fixed.FromInt(60), Fixed.FromInt(500), Fixed.FromInt(240));
        var way = grid.FindPath(fromX, fromY, toX, toY, LocomotorSurfaces.Ground);
        Check.That(way.Status == PathStatus.Found && way.Points.Any(point => point.Layer == 1) && way.Points[^1] == new PathPoint(toX, toY), $"The path crosses on the deck ({way.Status}).");
        var deck = way.Points.Where(point => point.Layer == 1).ToArray();
        Check.That(deck.All(point => point.Y >= Fixed.FromInt(130) && point.Y <= Fixed.FromInt(170)), "Its nodes on the layer lie on the deck.");
        var memo = grid.FindPath(fromX, fromY, toX, toY, LocomotorSurfaces.Ground);
        Check.That(memo.Points.SequenceEqual(way.Points), "A remembered answer keeps the layers.");
        Check.That(grid.FindPathToGoal(fromX, fromY, toX, toY, LocomotorSurfaces.Ground, []) is { Status: PathStatus.Found }, "The search that only counts when it arrives finds it as well.");
        var reached = grid.ReachableCells(fromX, fromY, LocomotorSurfaces.Ground);
        Check.That(grid.Reaches(reached, toX, toY) && grid.Reaches(reached, Fixed.FromInt(300), Fixed.FromInt(BridgeY)), "The flood goes over the bridge and finds the deck.");
        var fromDeck = grid.FindPath(Fixed.FromInt(300), Fixed.FromInt(BridgeY), toX, toY, LocomotorSurfaces.Ground, 1, 0);
        Check.That(fromDeck.Status == PathStatus.Found && fromDeck.Points[0].Layer == 1, "A unit on the deck walks off it.");
        var toDeck = grid.FindPath(fromX, fromY, Fixed.FromInt(300), Fixed.FromInt(BridgeY), LocomotorSurfaces.Ground, 0, 1);
        Check.That(toDeck.Status == PathStatus.Found && toDeck.Points[^1].Layer == 1, "A place on the deck is reached on the deck.");

        var boat = grid.FindPath(Fixed.FromInt(300), Fixed.FromInt(40), Fixed.FromInt(300), Fixed.FromInt(260), LocomotorSurfaces.Water);
        Check.That(boat.Status == PathStatus.Found && boat.Points.All(point => point.Layer == 0), "A boat sails under the bridge.");
        Check.That(world.LayerForGoal(Fixed.FromInt(300), Fixed.FromInt(BridgeY), LocomotorSurfaces.Water) == 0 && world.LayerForGoal(Fixed.FromInt(300), Fixed.FromInt(BridgeY), LocomotorSurfaces.Ground) == 1, "A goal on the deck is on the deck, except for a boat.");

        grid.SetBridgeUsable(1, false);
        Check.That(grid.FindPath(fromX, fromY, toX, toY, LocomotorSurfaces.Ground).Status == PathStatus.Partial, "With the layer closed no way leads across.");
        Check.That(grid.FindPathToGoal(fromX, fromY, toX, toY, LocomotorSurfaces.Ground, []) is null, "The arriving search proves it.");
        Check.That(!grid.Reaches(grid.ReachableCells(fromX, fromY, LocomotorSurfaces.Ground), toX, toY), "And so does the flood.");
        Check.That(grid.ConnectLayerAt(22, 15) == 0, "The ground no longer joins the layer.");
        grid.SetBridgeUsable(1, true);
        Check.That(grid.ConnectLayerAt(22, 15) == 1 && grid.FindPath(fromX, fromY, toX, toY, LocomotorSurfaces.Ground).Status == PathStatus.Found, "Open again, the way is there again.");
        Check.That(terrain.BridgeSpans.Length == 1, "The terrain itself never changes.");
    }

    /// <summary>A unit crosses on the deck: it takes the layer at the bridge, stands at the deck's height, and is on the ground again beyond it - in the same frame of the original at every rate; a world saved in the middle of the crossing goes on identically.</summary>
    public static void UnitsCrossTheDeck()
    {
        var results = new List<(long Up, long Arrived)>();
        foreach (var rate in BridgeRates)
        {
            var (world, feeder, fixture, terrain) = BridgeWorld(rate, RiverMap(SampleSpan()));
            var unit = BridgeUnit(world, feeder, "Crosser", 120, BridgeY);
            Check.That(feeder.SetMoveTarget(1, unit.Id.Value, 480, BridgeY).Accepted, "The order is accepted.");
            long up = -1, arrived = -1;
            var saved = false;
            for (var step = 0; step < world.Frames(1200) && arrived < 0; step++)
            {
                world.Step();
                // Read where a frame ends: inside one the rates stand at different shares of it.
                if (unit.Layer == 1 && up < 0 && world.Time.IsOriginalFrameBoundary(world.Tick)) up = world.InFrames(world.Tick);
                if (unit.Layer == 1 && unit.X > Fixed.FromInt(290) && unit.X < Fixed.FromInt(310))
                {
                    Check.That(unit.Z == Fixed.FromDecimal(10.25m), $"{rate}: the unit stands on the deck ({unit.Z}).");
                    if (!saved)
                    {
                        saved = true;
                        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
                        Check.That(restored.FindEntity(unit.Id)!.Layer == 1, $"{rate}: the layer is state.");
                        for (var follow = 0; follow < restored.Frames(120); follow++)
                        {
                            var (one, other) = (restored.Step().StateHash, world.Step().StateHash);
                            Check.That(one == other, $"{rate}: the restored world crosses as the live one (step {follow}).");
                        }
                    }
                }
                if (!unit.HasMoveTarget && world.InFrames(world.Tick) > 10 && world.Time.IsOriginalFrameBoundary(world.Tick)) arrived = world.InFrames(world.Tick);
            }
            Check.That(saved && up > 0 && arrived > up, $"{rate}: the unit went up and arrived ({up}, {arrived}).");
            Check.That(unit.Layer == 0 && unit.X > Fixed.FromInt(470) && unit.Z == Fixed.FromInt(10), $"{rate}: it is on the far bank, on the ground ({unit.X}, {unit.Z}, layer {unit.Layer}).");
            results.Add((up, arrived));
        }
        Check.That(results.Distinct().Count() == 1, $"The crossing takes the same frames at every rate ({string.Join(", ", results)}).");
    }

    /// <summary>A dry valley with banks of 25 units, a deck of 25.25 over it at y = 200.</summary>
    private static MapData ValleyMap()
    {
        var map = SyntheticMaps.Build(60, 40, 1, (x, _) => x <= 24 || x >= 36 ? 40 : x is >= 28 and <= 32 ? 0 : x < 28 ? 10 * (28 - x) : 10 * (x - 32));
        return map with { Objects = [BridgePoint("SampleSpan", 230m, 200m, true), BridgePoint("SampleSpan", 370m, 200m, false)] };
    }

    /// <summary>Under a deck high enough the ground stays open: a unit walks below it, on the ground, past one that stands on the deck above it.</summary>
    public static void UnitsWalkUnderADeck()
    {
        foreach (var rate in BridgeRates)
        {
            var (world, feeder, _, _) = BridgeWorld(rate, ValleyMap());
            var grid = world.Navigation!;
            Check.That(grid.IsPassable(30, 20, LocomotorSurfaces.Ground), $"{rate}: the valley floor under the deck stays open.");
            var above = BridgeUnit(world, feeder, "Crosser", 120, 200);
            Check.That(feeder.SetMoveTarget(1, above.Id.Value, 300, 200).Accepted, "The order onto the deck is accepted.");
            UntilArrived(world, above);
            Check.That(above.Layer == 1 && above.Z == Fixed.FromDecimal(25.25m) && (above.X - Fixed.FromInt(300)).Abs() < Fixed.FromInt(2), $"{rate}: one unit stands on the deck ({above.X}, {above.Z}, {above.Layer}).");
            var below = BridgeUnit(world, feeder, "Crosser", 300, 60);
            Check.That(below.Layer == 0, $"{rate}: the other one is on the ground.");
            Check.That(feeder.SetMoveTarget(1, below.Id.Value, 300, 340).Accepted, "The order under the deck is accepted.");
            var underneath = false;
            world.Step();
            for (var step = 0; step < world.Frames(900) && below.HasMoveTarget; step++)
            {
                world.Step();
                if (below.Y > Fixed.FromInt(190) && below.Y < Fixed.FromInt(210))
                {
                    underneath = true;
                    Check.That(below.Layer == 0 && below.Z == Fixed.Zero && (below.X - Fixed.FromInt(300)).Abs() < Fixed.FromInt(2), $"{rate}: it walks straight under the deck, on the valley floor ({below.X}, {below.Z}).");
                }
            }
            Check.That(underneath && below.Y > Fixed.FromInt(330), $"{rate}: it came through ({below.X}, {below.Y}).");
            Check.That(above.Z == Fixed.FromDecimal(25.25m), $"{rate}: the unit above never noticed.");
        }
    }

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, TerrainMap Terrain, TerrainBridge Bridge, Entity Span) LandmarkWorld(SimulationRate rate)
    {
        var (world, feeder, fixture, terrain) = BridgeWorld(rate, RiverMap(new MapObject("LandmarkSpan", 300m, BridgeY, 10.25m, 0m, 0, MapProperties.Empty)));
        var bridge = world.Bridges.Single();
        return (world, feeder, fixture, terrain, bridge, world.FindEntity(bridge.ObjectId)!);
    }

    /// <summary><c>Bridge::Bridge(Object*)</c>: a landmark bridge's box is its deck, and it has four towers at its corners, tied to it both ways; a terrain bridge cannot be destroyed at all.</summary>
    public static void LandmarkBridgesHaveTowersAndTerrainBridgesLast()
    {
        var (world, _, _, _, bridge, span) = LandmarkWorld(TestRate.Default);
        Check.That(bridge.RenderIndex == -1 && bridge.Layer == 1 && bridge.FromLeft.X == Fixed.FromInt(230) && bridge.FromLeft.Y == Fixed.FromInt(170), "The box of the object is the deck.");
        var towers = bridge.Towers.Select(id => world.FindEntity(id)!).ToArray();
        Check.That(towers.All(tower => tower.Template.Name == "SpanTower"), "It has four towers.");
        Check.That(towers[0].X == Fixed.FromInt(230) && towers[0].Y == Fixed.FromInt(175) && towers[1].Y == Fixed.FromInt(125), "They stand their radius out from the corners.");
        var behavior = span.Modules.OfType<BridgeBehavior>().Single();
        Check.That(Enumerable.Range(0, 4).All(type => behavior.TowerId(type) == towers[type].Id && towers[type].Modules.OfType<BridgeTowerBehavior>().Single() is var link && link.Bridge == span.Id && link.TowerType == type), "Bridge and towers know each other.");
        Check.That(!world.Navigation!.IsBlockedByObstacle(23, 17), "A tower is no obstacle.");

        var (terrainWorld, _, _, _) = BridgeWorld(TestRate.Default, RiverMap(SampleSpan()));
        var generic = terrainWorld.FindEntity(terrainWorld.Bridges[0].ObjectId)!;
        terrainWorld.Kill(generic.Id);
        terrainWorld.Step();
        Check.That(!generic.EffectivelyDead && terrainWorld.Bridges[0].DamageState == BodyDamageStates.Pristine && terrainWorld.Navigation!.IsBridgeUsable(1), "A GenericBridge has an ImmortalBody: nothing brings a terrain bridge down.");
    }

    /// <summary>
    /// <c>Bridge::updateDamageState</c>: a bridge that becomes rubble closes its layer, whatever is on it dies a fall, a unit on its way there stops at the river, and the bridge is healed back into use
    /// (an object in the rubble state can be healed). Below the deck nothing happens.
    /// </summary>
    public static void ADestroyedBridgeClosesItsLayer()
    {
        foreach (var rate in BridgeRates)
        {
            var (world, feeder, fixture, terrain, bridge, span) = LandmarkWorld(rate);
            var rider = BridgeUnit(world, feeder, "Crosser", 120, BridgeY);
            Check.That(feeder.SetMoveTarget(1, rider.Id.Value, 300, BridgeY).Accepted, "The order onto the deck is accepted.");
            UntilArrived(world, rider);
            Check.That(rider.Layer == 1, $"{rate}: a unit stands on the deck.");
            var boat = BridgeUnit(world, feeder, "Boat", 300, BridgeY);
            var walker = BridgeUnit(world, feeder, "Crosser", 120, 100);
            Check.That(feeder.SetMoveTarget(1, walker.Id.Value, 480, 100).Accepted, "The other unit is sent across.");
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            Check.That(walker.HasMoveTarget, $"{rate}: it is on its way.");

            world.Kill(span.Id);
            Check.That(bridge.DamageState == BodyDamageStates.Rubble && !world.Navigation!.IsBridgeUsable(1), $"{rate}: the bridge is rubble and its layer closed.");
            Check.That(rider.EffectivelyDead, $"{rate}: what was on the deck fell to its death.");
            Check.That(!boat.EffectivelyDead && boat.Layer == 0, $"{rate}: what was under it lives.");
            Check.That(bridge.Towers.All(id => world.FindEntity(id)!.EffectivelyDead), $"{rate}: the towers went with it.");
            UntilArrived(world, walker);
            Check.That(walker.X < Fixed.FromInt(RiverWest), $"{rate}: the walker's way ended at the river ({walker.X}).");
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
            Check.That(!restored.Navigation!.IsBridgeUsable(1) && restored.Bridges[0].ObjectId == bridge.ObjectId && restored.Bridges[0].Towers.SequenceEqual(bridge.Towers) && restored.Navigation.ObstacleSum == world.Navigation!.ObstacleSum, $"{rate}: the state of the bridge is saved.");
            for (var step = 0; step < world.Frames(10); step++) Check.That(restored.Step().StateHash == world.Step().StateHash, $"{rate}: the restored world goes on as the live one.");

            world.Damage(span.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(800), DamageTypes.Healing, DeathTypes.None, false));
            Check.That(bridge.DamageState == BodyDamageStates.Pristine && world.Navigation!.IsBridgeUsable(1) && !span.EffectivelyDead, $"{rate}: healed, it is back in use ({bridge.DamageState}).");
            Check.That(!span.Modules.OfType<BridgeBehavior>().Single().IsDestroyed, $"{rate}: and no longer dead.");
        }
    }

    /// <summary>
    /// <c>BridgeBehavior::createScaffolding</c> and the dozer's repair: behind the check the original cut (<c>canRepairObject</c>) a dozer sent to a broken bridge goes to its nearest tower, scaffolding
    /// rises and meets in the middle, the layer stays closed while it stands, and when the tower is healed the scaffolding goes and the layer opens - in the same frames at every rate.
    /// </summary>
    public static void ScaffoldsRepairABridgeBehindTheCut()
    {
        var frames = new List<(long Stood, long Done)>();
        foreach (var rate in BridgeRates)
        {
            var (world, feeder, fixture, terrain, bridge, span) = LandmarkWorld(rate);
            var dozer = BridgeUnit(world, feeder, "BridgeDozer", 180, BridgeY);
            var ai = dozer.Modules.OfType<DozerAIUpdate>().Single();
            var behavior = span.Modules.OfType<BridgeBehavior>().Single();
            var tower = world.FindEntity(bridge.Towers[0])!;
            world.Kill(span.Id);
            Check.That(!feeder.Repair(1, dozer.Id.Value, span.Id.Value).Accepted, $"{rate}: the original cut this: a repair of a bridge is refused.");
            ai.OrderRepair(world, span, fromPlayer: false);
            for (var step = 0; step < world.Frames(10); step++) world.Step();
            Check.That(!ai.HasBuilderTask, $"{rate}: and the dozer takes no such job.");

            world.BridgeRepairIsCut = false;
            Check.That(feeder.Repair(1, dozer.Id.Value, span.Id.Value).Accepted, $"{rate}: behind the cut the order is good.");
            world.Step();
            Check.That(ai.HasBuilderTask, $"{rate}: and the dozer takes the job.");
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            long stood = -1, done = -1;
            var inTheMiddle = false;
            for (var step = 0; step < world.Frames(6000) && done < 0; step++)
            {
                if (!inTheMiddle && world.Time.IsOriginalFrameBoundary(world.Tick) && behavior.IsScaffoldPresent && !behavior.IsScaffoldInMotion(world) && stood < 0)
                {
                    stood = world.InFrames(world.Tick);
                    inTheMiddle = true;
                    Check.That(!world.Navigation!.IsBridgeUsable(1), $"{rate}: while scaffolding stands the layer is closed.");
                    Check.That(behavior.Scaffolds.Count >= 6 && behavior.Scaffolds.All(id => world.FindEntity(id) is { Template.Name: "SpanScaffold" }), $"{rate}: scaffold objects and their supports stand ({behavior.Scaffolds.Count}).");
                    Check.That(behavior.Scaffolds.Max(id => world.FindEntity(id)!.Z) == Fixed.Zero + bridge.From.Z, $"{rate}: the top row stands at the deck.");
                    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
                    restored.BridgeRepairIsCut = false;
                    for (var follow = 0; follow < restored.Frames(4); follow++) Check.That(restored.Step().StateHash == world.Step().StateHash, $"{rate}: the scaffolding is state.");
                }
                world.Step();
                if (!ai.HasBuilderTask && tower.Body!.Health == tower.Body.MaxHealth && world.Time.IsOriginalFrameBoundary(world.Tick)) done = world.InFrames(world.Tick);
            }
            Check.That(stood > 0 && done > stood, $"{rate}: the scaffolding stood before the tower was whole ({stood}, {done}).");
            Check.That(!behavior.IsScaffoldPresent, $"{rate}: the scaffolding is told to go.");
            // The tower hands on its share of the health of each step, which rounds as it does in the original.
            Check.That(span.Body!.MaxHealth - span.Body.Health < Fixed.One && bridge.DamageState == BodyDamageStates.Pristine, $"{rate}: healing the tower healed the bridge ({span.Body.Health}, {bridge.DamageState}).");
            Check.That(world.Navigation!.IsBridgeUsable(1), $"{rate}: the layer is open again.");
            for (var step = 0; step < world.Frames(1200); step++) world.Step();
            Check.That(!world.Entities.Any(entity => entity.Template.Name == "SpanScaffold"), $"{rate}: the scaffolding sank away.");
            frames.Add((stood, done));
        }
        Check.That(frames.Distinct().Count() == 1, $"The repair takes the same frames at every rate ({string.Join(", ", frames)}).");
    }

    /// <summary>
    /// <c>DumbProjectileBehavior::update</c>: a shell lobbed at a unit under a deck blows up on the deck, in the same frame at every rate, and the unit below is not hurt.
    /// <c>BuildAssistant::isLocationLegalToBuild</c>: "we're on a bridge". A slave that stays on its master's layer follows it onto the deck.
    /// </summary>
    public static void DecksStopShellsAndBuildings()
    {
        var hits = new List<long>();
        foreach (var rate in BridgeRates)
        {
            var (valley, feeder, fixture, _) = BridgeWorld(rate, ValleyMap());
            var gun = BridgeUnit(valley, feeder, "DeckArtillery", 120, 200);
            Check.That(feeder.Spawn(2, "Crosser", owner: 2, x: 300, y: 200).Accepted, "The victim spawns.");
            valley.Step();
            while (!valley.Time.IsOriginalFrameBoundary(valley.Tick)) valley.Step();
            var victim = valley.Entities.Single(value => value.Template.Name == "Crosser");
            Check.That(victim.Layer == 0 && victim.Z == Fixed.Zero, $"{rate}: the victim stands under the deck.");
            var fired = feeder.FireWeapon(1, gun.Id.Value, victim.Id.Value);
            Check.That(fired.Accepted, $"{rate}: the gun may fire ({fired}).");
            var impact = default(SimEvent?);
            for (var step = 0; step < valley.Frames(120) && impact is null; step++)
                impact = valley.Step().Events.Where(value => value.Kind == SimEventKind.WeaponImpact).Select(value => (SimEvent?)value).FirstOrDefault();
            Check.That(impact is { HasPosition: true } hit && hit.Z == Fixed.FromDecimal(27.25m) && hit.X > Fixed.FromInt(230) && hit.X < Fixed.FromInt(290), $"{rate}: the shell blows up on the deck, above its target ({impact?.X}, {impact?.Z}).");
            Check.That(victim.Body!.Health == victim.Body.MaxHealth, $"{rate}: what is under the deck is not hurt.");
            hits.Add(valley.InFrames(impact!.Value.Tick));

            Check.That(fixture.Templates.TryGet("GenericBridge", out var site), "A small building.");
            Check.That(valley.CheckBuildGround(site, Fixed.FromInt(300), Fixed.FromInt(210), Fixed.Zero) == LegalBuildCode.RestrictedTerrain, $"{rate}: nothing is built on a deck.");
            Check.That(valley.CheckBuildGround(site, Fixed.FromInt(300), Fixed.FromInt(120), Fixed.Zero) == LegalBuildCode.Ok, $"{rate}: beside it the same ground is a site ({valley.CheckBuildGround(site, Fixed.FromInt(300), Fixed.FromInt(120), Fixed.Zero)}).");

            var master = BridgeUnit(valley, feeder, "Crosser", 120, 240);
            var drone = BridgeUnit(valley, feeder, "DeckDrone", 110, 240);
            drone.Modules.OfType<ISlavedModule>().Single().OnEnslave(valley, master);
            Check.That(feeder.SetMoveTarget(1, master.Id.Value, 300, 205).Accepted, $"{rate}: the master walks onto the deck.");
            UntilArrived(valley, master);
            for (var step = 0; step < valley.Frames(60); step++) valley.Step();
            Check.That(master.Layer == 1 && drone.Layer == 1, $"{rate}: the slave is on its master's layer ({master.Layer}, {drone.Layer}).");
        }
        Check.That(hits.Distinct().Count() == 1, $"The shell lands in the same frame at every rate ({string.Join(", ", hits)}).");

        var (world, _, _, _) = BridgeWorld(TestRate.Default, RiverMap(SampleSpan()));
        Check.That(world.ProjectileHitsDeck(Fixed.FromInt(300), Fixed.FromInt(BridgeY), Fixed.FromInt(20), Fixed.FromInt(5), out var deck) && deck == Fixed.FromDecimal(12.25m), $"A falling shell meets the deck ({deck}).");
        Check.That(!world.ProjectileHitsDeck(Fixed.FromInt(300), Fixed.FromInt(BridgeY), Fixed.FromInt(5), Fixed.FromInt(1), out _), "One below it does not.");
        Check.That(!world.ProjectileHitsDeck(Fixed.FromInt(300), Fixed.FromInt(100), Fixed.FromInt(20), Fixed.FromInt(5), out _), "Beside it neither.");
    }

    /// <summary>A real map: units cross the river of a shipped skirmish map on its bridge, a path over it is found, and with the bridge down there is none.</summary>
    public static void RealDataUnitsCrossABridge(string contentDirectory)
    {
        var content = ContentFileSystem.Open(contentDirectory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        // This program reads no models: the width of the decks is a stand-in here, the real table is checked by the game's tests.
        var decks = BridgeCatalog.Build(graph).Bridges.Select(block => new KeyValuePair<string, BridgeDeck>(block.Name, new BridgeDeck(Fixed.FromInt(-25), Fixed.FromInt(25))));
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, new ModelGeometry([], [], [], decks), CnCNew.Simulation.Rules.RuleProfile.Product);
        Check.That(catalog.Bridges.Bridges.Length == 27 && catalog.Bridges.TryGet("KandaharBridge", out var kandahar) && kandahar.Towers.All(name => name.Length > 0) && kandahar.ScaffoldObject == "BridgeScaffold01", "The Bridge blocks of Roads.ini are typed.");
        MapData map;
        using (var stream = content.OpenRead("Maps/Bridge Busters/Bridge Busters.map")) map = CnCNew.Formats.SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, "Bridge Busters");
        Check.That(terrain.BridgeSpans.Length >= 1, "The map has bridges.");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), catalog.Rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fingerprint, terrain.Fingerprint), catalog, terrain);
        Check.That(world.Bridges.Count == terrain.BridgeSpans.Length, $"Every pair of points is a bridge ({world.Bridges.Count} of {terrain.BridgeSpans.Length}).");
        Check.That(world.Bridges.All(bridge => world.FindEntity(bridge.ObjectId)?.Template.Name == "GenericBridge"), "Each has its object.");
        var grid = world.Navigation!;
        var bridge = world.Bridges[0];
        var (ux, uy) = Unit(bridge.To.X - bridge.From.X, bridge.To.Y - bridge.From.Y);
        var reach = Fixed.FromInt(60);
        var (startX, startY) = (bridge.From.X - ux * reach, bridge.From.Y - uy * reach);
        var (goalX, goalY) = (bridge.To.X + ux * reach, bridge.To.Y + uy * reach);
        var way = grid.FindPath(startX, startY, goalX, goalY, LocomotorSurfaces.Ground);
        Check.That(way.Status == PathStatus.Found && way.Points.Any(point => point.Layer == bridge.Layer), $"A path leads over the bridge ({way.Status}, {way.Points.Length} nodes).");
        foreach (var other in world.Bridges) grid.SetBridgeUsable(other.Layer, false);
        var without = grid.FindPath(startX, startY, goalX, goalY, LocomotorSurfaces.Ground);
        Check.That(without.Status == PathStatus.Partial && !grid.Reaches(grid.ReachableCells(startX, startY, LocomotorSurfaces.Ground), goalX, goalY), $"With the bridges down there is no way ({without.Status}).");
        foreach (var other in world.Bridges) grid.SetBridgeUsable(other.Layer, true);

        Check.That(catalog.TryGet("AmericaVehicleHumvee", out var humvee), "The data has a Humvee.");
        var feeder = new Feeder(world);
        Check.That(feeder.Spawn(1, humvee.Name, owner: 1, x: (int)startX.Floor(), y: (int)startY.Floor()).Accepted, "The Humvee spawns.");
        world.Step();
        var unit = world.Entities.Single(entity => entity.Template.Name == humvee.Name);
        Check.That(feeder.SetMoveTarget(1, unit.Id.Value, (int)goalX.Floor(), (int)goalY.Floor()).Accepted, "The order is accepted.");
        var onDeck = false;
        SimulationWorld? restored = null;
        world.Step();
        for (var step = 0; step < world.Frames(3000) && unit.HasMoveTarget; step++)
        {
            var hash = world.Step().StateHash;
            if (restored is not null) Check.That(restored.Step().StateHash == hash, $"The world saved in the middle of the crossing goes on identically (step {step}).");
            if (unit.Layer != bridge.Layer) continue;
            onDeck = true;
            if (bridge.IsPointOnBridge(unit.X, unit.Y))
            {
                Check.That((unit.Z - bridge.HeightAt(unit.X, unit.Y)).Abs() < Fixed.FromInt(3), $"It drives on the deck ({unit.Z}).");
                restored ??= WorldSnapshot.Restore(WorldSnapshot.Write(world), catalog, fingerprint, terrain);
            }
        }
        Check.That(restored is not null, "The crossing was saved.");
        var (leftX, leftY) = (goalX - unit.X, goalY - unit.Y);
        Check.That(onDeck && leftX.Abs() + leftY.Abs() < Fixed.FromInt(20) && unit.Layer == 0, $"It crossed on the bridge and stands at its goal, on the ground ({unit.X}, {unit.Y}, layer {unit.Layer}).");

        // A landmark bridge of a shipped map: an object with four towers, and a bridge of the terrain as well.
        using (var stream = content.OpenRead("Maps/Alpine Assault/Alpine Assault.map")) map = CnCNew.Formats.SageMapDecoder.Decode(stream);
        var alpine = TerrainMap.Build(map, "Alpine Assault");
        var landmarkWorld = new SimulationWorld(new WorldConfiguration(new WorldId(2), catalog.Rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fingerprint, alpine.Fingerprint), catalog, alpine);
        var landmark = landmarkWorld.Bridges.Single(value => value.RenderIndex < 0);
        Check.That(landmarkWorld.FindEntity(landmark.ObjectId)!.Template.IsBridge && landmark.Towers.All(id => landmarkWorld.FindEntity(id)?.Template.IsKindOf("BRIDGE_TOWER") == true), "The landmark bridge has its four towers.");
    }

    private static (Fixed X, Fixed Y) Unit(Fixed x, Fixed y)
    {
        var length = Fixed.Sqrt(x * x + y * y);
        return (x / length, y / length);
    }

    private static Fixed Length(PathResult path)
    {
        var total = Fixed.Zero;
        for (var index = 1; index < path.Points.Length; index++)
        {
            var (dx, dy) = (path.Points[index].X - path.Points[index - 1].X, path.Points[index].Y - path.Points[index - 1].Y);
            total += Fixed.Sqrt(dx * dx + dy * dy);
        }
        return total;
    }
}

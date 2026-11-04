using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const string MapObjectsIni = """
        Object MapHut
          KindOf = STRUCTURE IMMOBILE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 400.0
            InitialHealth = 400.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object MapWreck
          KindOf = STRUCTURE IMMOBILE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 400.0
            InitialHealth = 400.0
          End
        End
        Object MapTree
          KindOf = OPTIMIZED_TREE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object MapProp
          KindOf = PROP
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object MapFluff
          KindOf = CLEARED_BY_BUILD
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object MapFence
          KindOf = CLEARED_BY_BUILD
          FenceWidth = 20.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        """;

    private static Dictionary<string, string> MapObjectFiles() => new(GroupMoveFiles()) { ["Data/INI/Object/MapObjects.ini"] = MapObjectsIni };

    /// <summary>
    /// <c>GameLogic::startNewGame</c>, <c>Object::updateObjValuesFromMapProperties</c> (F-129): a placed map object gets the map's health, an indestructible body takes no damage, an unsellable one is
    /// not sold, its script statuses and its AI's attitude are set, and a save keeps all of it. Trees, props and fluff are drawn only, a fence is a logic object; only the system places one;
    /// the command survives both codecs. At 30, 60 and 120 Hz.
    /// </summary>
    public static void MapObjectsComeWithTheirProperties()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, _, fixture) = RatedWorld(rate, MapObjectFiles(), NavTerrain());
            ulong sequence = 100;
            PlaceMapObjectCommand Place(string template, int owner, int x, int health, MapObjectFlags flags, int attitude = 0)
                => new(PlayerId.System, sequence++, world.Tick, template, owner == 0 ? PlayerId.System : new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(100), Fixed.Zero, Fixed.Zero, health, flags, attitude);
            Check.That(world.Submit(Place("MapHut", 1, 40, 50, MapObjectFlags.Indestructible | MapObjectFlags.Unsellable | MapObjectFlags.Targetable)).Accepted, "The system places an indestructible hut.");
            Check.That(world.Submit(Place("MapHut", 1, 100, 100, MapObjectFlags.None)).Accepted, "The system places a plain hut.");
            Check.That(world.Submit(Place("Marcher", 0, 150, 100, MapObjectFlags.Disabled, -2)).Accepted, "The system places a sleeping soldier.");
            // Scorched Earth places wrecks at objectInitialHealth 0: rubble before they stand (the grid gave a footprint that was never there back, and overflowed).
            Check.That(world.Submit(Place("MapWreck", 1, 170, 0, MapObjectFlags.None)).Accepted, "The system places a wreck.");
            Check.That(!world.Submit(new PlaceMapObjectCommand(new PlayerId(1), 1, world.Tick, "MapHut", new PlayerId(1), Fixed.FromInt(10), Fixed.FromInt(10), Fixed.Zero, Fixed.Zero, 100, MapObjectFlags.None, 0)).Accepted,
                "A player places nothing.");
            world.Step();
            var wreck = world.Entities.Single(entity => entity.Template.Name == "MapWreck");
            Check.That(wreck.IsRubble && wreck.Body!.Health == Fixed.Zero, $"{rate}: a wreck at 0 % stands as rubble.");
            var huts = world.Entities.Where(entity => entity.Template.Name == "MapHut").OrderBy(entity => entity.X).ToArray();
            var soldier = world.Entities.Single(entity => entity.Template.Name == "Marcher");
            Check.That(huts.Length == 2 && huts[0].Body!.Health == Fixed.FromInt(200) && huts[1].Body!.Health == Fixed.FromInt(400), $"{rate}: the map's health ({huts[0].Body?.Health}).");
            Check.That((huts[0].ScriptDisableMask & (Entity.ScriptUnsellable | Entity.ScriptTargetable)) == (Entity.ScriptUnsellable | Entity.ScriptTargetable) && huts[1].ScriptDisableMask == 0,
                $"{rate}: the script statuses of the map.");
            Check.That(soldier.OwnAttitude == -2 && (soldier.ScriptDisableMask & Entity.ScriptDisabled) != 0, $"{rate}: the soldier sleeps and is disabled ({soldier.OwnAttitude}).");
            // The system's own sequence numbers: the placements above took 100 to 103.
            foreach (var hut in huts) Check.That(world.Submit(new DamageCommand(PlayerId.System, sequence++, world.Tick, hut.Id, Fixed.FromInt(150), 0, 0)).Accepted, "The damage is accepted.");
            world.Step();
            Check.That(huts[0].Body!.Health == Fixed.FromInt(200) && huts[1].Body!.Health == Fixed.FromInt(250), $"{rate}: the indestructible hut takes nothing ({huts[0].Body?.Health}, {huts[1].Body?.Health}).");
            Check.That(!world.Submit(new SellCommand(new PlayerId(1), 50, world.Tick, huts[0].Id)).Accepted && world.Submit(new SellCommand(new PlayerId(1), 51, world.Tick, huts[1].Id)).Accepted,
                $"{rate}: the unsellable hut is not sold, the other is.");
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, NavTerrain());
            Check.That(restored.StateHash == world.StateHash, $"{rate}: a save keeps the placed objects.");
            var again = restored.Entities.Single(entity => entity.Id == huts[0].Id);
            Check.That(again.Body is CnCNew.Simulation.Modules.BuiltIn.ActiveBody { Indestructible: true } && again.ScriptDisableMask == huts[0].ScriptDisableMask, $"{rate}: restored, still indestructible.");
        }

        var command = new PlaceMapObjectCommand(PlayerId.System, 7, 0, "MapHut", PlayerId.System, Fixed.FromInt(3), Fixed.FromInt(4), Fixed.One, Fixed.FromDecimal(0.5m), 75,
            MapObjectFlags.Indestructible | MapObjectFlags.Targetable, -2);
        Check.That(ReplayFormat.DecodeCommand(ReplayFormat.EncodeCommand(command)) == command, "The replay codec carries the placement.");
        Check.That(CommandTypes.IsKnown(CommandTypes.PlaceMapObject) && !CommandTypes.IsPlayerOrder(CommandTypes.PlaceMapObject), "Only the system places map objects.");

        var fixture2 = new CnCNew.Simulation.Testing.SyntheticFixture(TestRate.Default, files: MapObjectFiles());
        MapObjectPlacement.Kind Kind(string name, MapProperties? properties = null)
            => MapObjectPlacement.Classify(new MapObject(name, 0m, 0m, 0m, 0m, 0, properties ?? MapProperties.Empty), fixture2.Templates, out _);
        Check.That(Kind("MapHut") == MapObjectPlacement.Kind.Placed && Kind("MapFence") == MapObjectPlacement.Kind.Placed, "Structures and fences are logic objects.");
        Check.That(Kind("MapTree") == MapObjectPlacement.Kind.DrawnOnly && Kind("MapProp") == MapObjectPlacement.Kind.DrawnOnly && Kind("MapFluff") == MapObjectPlacement.Kind.DrawnOnly,
            "Optimized trees, props and fluff are drawn only.");
        Check.That(Kind("NoSuchThing") == MapObjectPlacement.Kind.Unknown, "An unknown template is counted.");
        var properties = new MapProperties([new MapProperty("objectIndestructible", MapPropertyType.Bool, 1, 0m, ""), new MapProperty("objectEnabled", MapPropertyType.Bool, 1, 0m, ""),
            new MapProperty("objectPowered", MapPropertyType.Bool, 0, 0m, ""), new MapProperty("objectInitialHealth", MapPropertyType.Int, 50, 0m, ""), new MapProperty("objectAggressiveness", MapPropertyType.Int, -2, 0m, "")]);
        Check.That(MapObjectPlacement.Flags(properties) == (MapObjectFlags.Indestructible | MapObjectFlags.Unpowered) && MapObjectPlacement.HealthPercent(properties) == 50 && MapObjectPlacement.Attitude(properties) == -2,
            "The properties read as the original reads them.");
    }
}

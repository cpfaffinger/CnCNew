using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The Chinook's combat drop (<c>ChinookAIUpdate::privateCombatDrop</c>, <c>ChinookCombatDropState</c>, <c>AIRappelState</c>). The button of the America Chinook led nowhere before: the command did
/// not exist.
/// </summary>
static partial class Tests
{
    private const string CombatDropIni = """
        Locomotor DropWings
          Surfaces = AIR
          Speed = 120.0
          SpeedDamaged = 120.0
          TurnRate = 360.0
          Acceleration = 600.0
          Braking = 600.0
          PreferredHeight = 100.0
          Appearance = HOVER
        End
        Object DropChinook
          KindOf = SELECTABLE VEHICLE AIRCRAFT TRANSPORT
          Locomotor = SET_NORMAL DropWings
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = ChinookAIUpdate AI01
            NumRopes = 2
            PerRopeDelayMin = 900
            PerRopeDelayMax = 1500
            RopeFinalHeight = 10
            RappelSpeed = 30
            MinDropHeight = 40
          End
          Behavior = TransportContain Transport01
            Slots = 4
          End
        End
        Object DropRanger
          KindOf = SELECTABLE INFANTRY CAN_RAPPEL
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object DropCrate
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object DropBunker
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 30.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 60.0
          Body = StructureBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 5
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, Entity Chinook) DropWorld(SimulationRate rate, int riders)
    {
        var ropes = ImmutableArray.Create(
            new ExitPath(new BonePose(Fixed.FromInt(5), Fixed.FromInt(5), Fixed.FromInt(-2), Fixed.Zero), new BonePose(Fixed.FromInt(5), Fixed.FromInt(8), Fixed.FromInt(-4), Fixed.Zero)),
            new ExitPath(new BonePose(Fixed.FromInt(5), Fixed.FromInt(-5), Fixed.FromInt(-2), Fixed.Zero), new BonePose(Fixed.FromInt(5), Fixed.FromInt(-8), Fixed.FromInt(-4), Fixed.Zero)));
        var geometry = new ModelGeometry([], [], [], null, null, [new KeyValuePair<string, ImmutableArray<ExitPath>>("DropChinook", ropes)]);
        var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/CombatDrop.ini"] = CombatDropIni }, geometry: geometry);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "DropChinook", owner: 1, x: 100, y: 100, z: 100);
        for (var index = 0; index < riders; index++) feeder.Spawn(1, "DropRanger", owner: 1, x: 100, y: 100);
        world.Step();
        var chinook = world.Entities.Single(value => value.Template.Name == "DropChinook");
        foreach (var rider in world.Entities.Where(value => value.Template.Name == "DropRanger").ToArray()) Check.That(world.TryEnterContainer(rider, chinook), "A ranger boards.");
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        return (world, feeder, fixture, chinook);
    }

    private static CombatDropCommand DropOrder(SimulationWorld world, Entity chinook, EntityId target, int x, int y)
        => new(chinook.Owner, 900, world.Tick, [chinook.Id], target, Fixed.FromInt(x), Fixed.FromInt(y));

    private static readonly SimulationRate[] DropRates = [SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120)];

    public static void CombatDropAtAPlace()
    {
        var down = new List<long>();
        foreach (var rate in DropRates)
        {
            var (world, feeder, fixture, chinook) = DropWorld(rate, 3);
            var rangers = world.Entities.Where(value => value.Template.Name == "DropRanger").ToArray();
            Check.That(world.Submit(DropOrder(world, chinook, EntityId.Invalid, 300, 100)).Accepted, $"{rate}: the order is taken.");
            var saved = false;
            var restored = world;
            var inAir = false;
            for (var step = 0; step < world.Frames(1500) && rangers.Any(value => restored.FindEntity(value.Id) is { } left && (left.ContainedBy.IsValid || left.Z > Fixed.Zero)); step++)
            {
                restored.Step();
                var hanging = rangers.Select(value => restored.FindEntity(value.Id)!).FirstOrDefault(value => !value.ContainedBy.IsValid && value.Z > Fixed.Zero);
                if (hanging is null) continue;
                inAir = true;
                Check.That(hanging.X >= Fixed.FromInt(299) && hanging.X <= Fixed.FromInt(310), $"{rate}: they come down at the place ({hanging.X}).");
                if (saved) continue;
                saved = true;
                var hash = restored.StateHash;
                restored = WorldSnapshot.Restore(WorldSnapshot.Write(restored), fixture.Templates, fixture.Fingerprint);
                Check.That(restored.StateHash == hash, $"{rate}: a save in the middle of the drop keeps it.");
            }
            Check.That(inAir, $"{rate}: somebody came down a rope.");
            foreach (var ranger in rangers.Select(value => restored.FindEntity(value.Id)!))
            {
                Check.That(!ranger.ContainedBy.IsValid && ranger.Z == Fixed.Zero && !ranger.EffectivelyDead, $"{rate}: every ranger stands on the ground ({ranger.Z}, in {ranger.ContainedBy}).");
                Check.That(ranger.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.AIUpdateInterface>().All(ai => !ai.IsRappelling), $"{rate}: and is off the rope.");
            }
            down.Add(restored.InFrames(restored.Tick));
            var left2 = restored.FindEntity(chinook.Id)!;
            for (var step = 0; step < restored.Frames(60); step++) restored.Step();
            Check.That(left2.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.AIUpdateInterface>().All(ai => ai.Ropes.Count == 0), $"{rate}: the ropes are gone when the last is down.");
        }
        Check.That(down.Max() - down.Min() <= 2, $"The last ranger stands in the same frame at every rate ({string.Join(", ", down)}).");
    }

    public static void CombatDropIntoABuilding()
    {
        foreach (var rate in DropRates)
        {
            var (world, feeder, _, chinook) = DropWorld(rate, 1);
            feeder.Spawn(2, "DropBunker", owner: 2, x: 260, y: 100);
            feeder.Spawn(2, "DropCrate", owner: 2, x: 260, y: 100);
            world.Step();
            var bunker = world.Entities.Single(value => value.Template.Name == "DropBunker");
            var enemy = world.Entities.Single(value => value.Template.Name == "DropCrate");
            Check.That(world.TryEnterContainer(enemy, bunker), "The enemy sits in the bunker.");
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var ranger = world.Entities.Single(value => value.Template.Name == "DropRanger");
            Check.That(world.Submit(DropOrder(world, chinook, bunker.Id, 0, 0)).Accepted, $"{rate}: the drop into the bunker is taken.");
            var height = Fixed.Zero;
            for (var step = 0; step < world.Frames(1500) && !ranger.ContainedBy.Equals(bunker.Id); step++)
            {
                world.Step();
                if (chinook.Z > height) height = chinook.Z;
            }
            Check.That(height == Fixed.FromInt(100), $"{rate}: the Chinook flies over the building at its height, 60 + 40 ({height}).");
            Check.That(ranger.ContainedBy == bunker.Id, $"{rate}: the ranger is in the building.");
            Check.That(enemy.EffectivelyDead, $"{rate}: and the enemy in it is dead.");
        }
    }
}

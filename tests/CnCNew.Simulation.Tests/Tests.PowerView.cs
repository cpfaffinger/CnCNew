using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// F-59: SpecialPowerModule::createViewObject - a power triggered at a place opens a view there for its owner, the GameData's SpecialPowerViewObject with the power's
// ViewObjectRange as its shroud clearing range, gone after ViewObjectDuration.
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> PowerViewFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/GameData.ini"] = "GameData\n  SpecialPowerViewObject = ViewPing\nEnd\n",
        ["Data/INI/Object/PowerViewFixture.ini"] = """
        SpecialPower ViewPower
          Enum = SPECIAL_DAISY_CUTTER
          ReloadTime = 100
          ViewObjectDuration = 3000
          ViewObjectRange = 250
        End

        SpecialPower BlindPower
          Enum = SPECIAL_DAISY_CUTTER
          ReloadTime = 100
        End

        Object ViewPing
          ShroudClearingRange = 150.0
          KindOf = NO_COLLIDE IMMOBILE UNATTACKABLE INERT
          Body = ImmortalBody Body01
            MaxHealth = 1
            InitialHealth = 1
          End
          Behavior = DeletionUpdate Deletion01
            MinLifetime = 10000
            MaxLifetime = 10000
          End
        End

        Object ViewSilo
          KindOf = SELECTABLE STRUCTURE
          ShroudClearingRange = 50.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = OCLSpecialPower Ocl01
            SpecialPowerTemplate = ViewPower
            OCL = OCL_Wreck
          End
        End

        Object BlindSilo
          KindOf = SELECTABLE STRUCTURE
          ShroudClearingRange = 50.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = OCLSpecialPower Ocl01
            SpecialPowerTemplate = BlindPower
            OCL = OCL_Wreck
          End
        End
        """,
    };

    public static void PowerOpensItsViewAtTheTarget()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: PowerViewFiles());
        TerrainMap Map() => TerrainMap.Build(SyntheticMaps.Build(100, 100, 1, (_, _) => 0, null, null), "power-view");
        var terrain = Map();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        var player = new PlayerId(1);
        feeder.Spawn(1, "ViewSilo", owner: 1, x: 100, y: 100);
        feeder.Spawn(1, "BlindSilo", owner: 1, x: 160, y: 100);
        for (var step = 0; step < world.Frames(10); step++) world.Step();
        var silo = world.Entities.Single(value => value.Template.Name == "ViewSilo");
        var blind = world.Entities.Single(value => value.Template.Name == "BlindSilo");
        IEnumerable<Entity> Pings() => world.Entities.Where(value => value.Template.Name == "ViewPing" && !value.DestroyRequested);

        Check.That(!world.IsPositionVisibleTo(player, Fixed.FromInt(800), Fixed.FromInt(600)), "Setup: the target area is not seen.");
        Check.That(feeder.UseSpecialPower(1, blind.Id.Value, "BlindPower", SpecialPowerTargetKind.Location, x: 600, y: 600).Accepted, "The power without a view is used.");
        for (var step = 0; step < world.Frames(3); step++) world.Step();
        Check.That(!Pings().Any(), "A power without ViewObjectRange and ViewObjectDuration opens no view.");

        Check.That(feeder.UseSpecialPower(1, silo.Id.Value, "ViewPower", SpecialPowerTargetKind.Location, x: 600, y: 600).Accepted, "The power is used at a place.");
        var used = world.Tick;
        for (var step = 0; step < world.Frames(3); step++) world.Step();
        var ping = Pings().SingleOrDefault();
        Check.That(ping is not null && ping.Owner == player && ping.X == Fixed.FromInt(600) && ping.Y == Fixed.FromInt(600), "The view object stands at the target for the power's owner.");
        Check.That(ping!.ShroudRange == Fixed.FromInt(250), $"Its clearing range is the power's ViewObjectRange ({ping.ShroudRange}).");
        Check.That(world.IsPositionVisibleTo(player, Fixed.FromInt(800), Fixed.FromInt(600)), "The owner sees 200 away from the target - beyond the object's own range of 150.");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, Map());
        Check.That(restored.StateHash == world.StateHash, "The view restores to the same hash.");

        while (world.Tick < used + world.Frames(88)) world.Step();
        Check.That(Pings().Any(), "The view is still open just before ViewObjectDuration (3 s) is over.");
        while (world.Tick < used + world.Frames(95)) world.Step();
        Check.That(!Pings().Any() && !world.IsPositionVisibleTo(player, Fixed.FromInt(800), Fixed.FromInt(600)), "After ViewObjectDuration the view is gone, and the area with it.");
    }
}

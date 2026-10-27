using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const string MineBody = """
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 2.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
    """;

    private const string LayerBody = """
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
    """;

    private static IReadOnlyDictionary<string, string> MinefieldFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/GameData.ini"] = "GameData\n  BaseRegenHealthPercentPerSecond = 12.5%\n  BaseRegenDelay = 1000\nEnd\n",
        ["Data/INI/Upgrade.ini"] = """
        Upgrade Upgrade_Mines
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_EMPMines
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        """,
        ["Data/INI/Object/Minefield.ini"] =
            "Object TestMine\n" + MineBody + "\nEnd\n" +
            "Object EmpMine\n" + MineBody + "\nEnd\n" +
            """
            Object BadMine
              Geometry = CYLINDER
              GeometryMajorRadius = 5.0
              GeometryMinorRadius = 5.0
              GeometryHeight = 2.0
              Behavior = SpawnBehavior Mine01
                CanReclaimOrphans = Yes
              End
            End
            Object Wall
              KindOf = STRUCTURE
              Geometry = CYLINDER
              GeometryMajorRadius = 5.0
              GeometryMinorRadius = 5.0
              GeometryHeight = 5.0
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = DestroyDie Destroy01
              End
            End
            Object Truck
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = PhysicsBehavior Physics01
                Mass = 25.0
                AllowBouncing = Yes
                ForwardFriction = 2.0
              End
              Behavior = SquishCollide Squish01
              End
            End

            """ +
            "Object Minelayer\n  KindOf = STRUCTURE\n" + LayerBody + "\n" + """
              Behavior = GenerateMinefieldBehavior Mines01
                TriggeredBy = Upgrade_Mines
                MineName = TestMine
                UpgradedMineName = EmpMine
                UpgradedTriggeredBy = Upgrade_EMPMines
                Upgradable = Yes
                DistanceAroundObject = 40.0
                AlwaysCircular = Yes
                SmartBorder = Yes
                GenerationFX = FX_Test
              End
            End

            """ +
            "Object DeathLayer\n" + LayerBody + "\n" + """
              Behavior = GenerateMinefieldBehavior Mines01
                MineName = TestMine
                GenerateOnlyOnDeath = Yes
                DistanceAroundObject = 40.0
                AlwaysCircular = Yes
                SmartBorder = Yes
              End
            End

            """ +
            "Object BadLayer\n" + LayerBody + "\n" + """
              Behavior = GenerateMinefieldBehavior Mines01
                TriggeredBy = Upgrade_Mines
                MineName = BadMine
                GenerationFX = FX_Test
                AlwaysCircular = Yes
                SmartBorder = Yes
              End
            End

            """ +
            "Object PlainLayer\n" + LayerBody + "\n" + """
              Behavior = GenerateMinefieldBehavior Mines01
                TriggeredBy = Upgrade_Mines
                MineName = TestMine
                GenerationFX = FX_Test
              End
            End
            """,
    };

    private static int CountOf(SimulationWorld world, string template) => world.Entities.Count(value => value.Template.Name == template);

    /// <summary>
    /// <c>PhysicsBehavior</c> and <c>SquishCollide</c> are inert (no forces or collisions exist yet) but must bind and keep an
    /// object instantiable; <c>GenerateMinefieldBehavior</c> lays the reference's ring layout on its upgrade, swaps mines on
    /// the second upgrade, lays on death, and reports every mine it cannot create.
    /// </summary>
    public static void MatchMinefieldAndPhysics()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: MinefieldFiles());
        Check.That(fixture.Templates.TryGet("Truck", out var truck) && truck.IsInstantiable, "PhysicsBehavior and SquishCollide leave a vehicle instantiable: " + (truck is null ? "missing" : string.Join(", ", truck.MissingModules)));
        Check.That(fixture.Templates.TryGet("BadMine", out var badMine) && !badMine.IsInstantiable, "A mine with a module that is not implemented stays non-instantiable.");

        // ---- ring layout, FX request, upgrade swap
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Truck");
        feeder.Spawn(1, "Minelayer");
        world.Step();
        var layer = world.FindEntity(new EntityId(2))!;
        feeder.GiveMoney(1, 100);
        world.Step();
        var events = new List<SimEvent>();
        void Step(int ticks) { for (var i = 0; i < ticks; i++) events.AddRange(world.Step().Events); }
        Step(3);
        Check.That(CountOf(world, "TestMine") == 0, "No mines before the trigger upgrade.");
        feeder.GrantUpgrade(1, "Upgrade_Mines", target: 2);
        Step(3);
        // radius 20 layer + 5 mine radius: ring 1 at 25 (16 mines), ring 2 at 35 (22 mines), stop at 45 >= 40.
        Check.That(CountOf(world, "TestMine") == 38, "Two rings of 16 and 22 mines: " + CountOf(world, "TestMine"));
        Check.That(events.Count(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Test") == 1, "The generation FX is requested once.");
        var mines = world.Entities.Where(value => value.Template.Name == "TestMine").ToArray();
        var inner = mines.Count(value => ((value.X - layer.X) * (value.X - layer.X) + (value.Y - layer.Y) * (value.Y - layer.Y) - Fixed.FromInt(625)).Abs() < Fixed.FromInt(2));
        Check.That(inner == 16, "Sixteen mines sit on the radius-25 ring: " + inner);
        var orientations = mines.Select(value => value.Angle.Raw).Distinct().Count();
        Check.That(orientations > 30, "Every mine draws its own orientation: " + orientations);

        feeder.GrantUpgrade(1, "Upgrade_Mines", target: 2);
        Step(3);
        Check.That(CountOf(world, "TestMine") == 38, "Repurchasing does not lay a second field.");

        feeder.GrantUpgrade(1, "Upgrade_EMPMines", target: 2);
        Step(4);
        Check.That(CountOf(world, "TestMine") == 0 && CountOf(world, "EmpMine") == 38, $"The upgrade replaces the old mines: {CountOf(world, "TestMine")} old, {CountOf(world, "EmpMine")} new.");

        // ---- structure veto: a wall on the radius-25 ring removes exactly the mine that would sit under it
        var walled = NewWorld(fixture);
        var walledFeeder = new Feeder(walled);
        walledFeeder.Spawn(1, "Minelayer", x: 0, y: 0);
        walledFeeder.Spawn(1, "Wall", x: 25, y: 0);
        walled.Step();
        walledFeeder.GiveMoney(1, 100);
        walled.Step();
        walledFeeder.GrantUpgrade(1, "Upgrade_Mines", target: 1);
        for (var i = 0; i < 3; i++) walled.Step();
        Check.That(CountOf(walled, "TestMine") == 37, "A mine mostly under a structure is vetoed: " + CountOf(walled, "TestMine"));

        // ---- GenerateOnlyOnDeath
        var dying = NewWorld(fixture);
        var dyingFeeder = new Feeder(dying);
        dyingFeeder.Spawn(1, "DeathLayer");
        dying.Step();
        dyingFeeder.Damage(1, 1000);
        for (var i = 0; i < 4; i++) dying.Step();
        Check.That(CountOf(dying, "TestMine") == 38, "GenerateOnlyOnDeath lays the field when the owner dies: " + CountOf(dying, "TestMine"));

        // ---- non-instantiable mine and unmodeled layout are explicit events, never silent
        var bad = NewWorld(fixture);
        var badFeeder = new Feeder(bad);
        badFeeder.Spawn(1, "BadLayer");
        badFeeder.Spawn(1, "PlainLayer");
        bad.Step();
        badFeeder.GiveMoney(1, 100);
        bad.Step();
        badFeeder.GrantUpgrade(1, "Upgrade_Mines", target: 1);
        badFeeder.GrantUpgrade(1, "Upgrade_Mines", target: 2);
        var badEvents = new List<SimEvent>();
        for (var i = 0; i < 3; i++) badEvents.AddRange(bad.Step().Events);
        var skips = badEvents.Where(value => value.Kind == SimEventKind.CreationSkipped).ToArray();
        Check.That(skips.Length == 2 && skips.Any(value => value.Entity.Value == 1 && value.Text == "BadMine" && value.Value == CreationSkipReasons.TemplateNotInstantiable)
            && skips.Any(value => value.Entity.Value == 2 && value.Text.Contains("plain border") && value.Value == CreationSkipReasons.SettingNotSimulated),
            "A non-instantiable mine and an unmodeled layout each report one skip: " + string.Join("; ", skips.Select(value => $"{value.Entity.Value}:{value.Text}:{value.Value}")));
        Check.That(badEvents.Count(value => value.Kind == SimEventKind.FxRequested) == 1 && CountOf(bad, "TestMine") == 0,
            "The circular layer still requests its FX; the unmodeled layout requests none and lays no mine.");

        // ---- snapshot continuation across the upgrade swap
        var live = NewWorld(fixture);
        var liveFeeder = new Feeder(live);
        liveFeeder.Spawn(1, "Truck");
        liveFeeder.Spawn(1, "Minelayer");
        live.Step();
        liveFeeder.GiveMoney(1, 100);
        live.Step();
        liveFeeder.GrantUpgrade(1, "Upgrade_Mines", target: 2);
        for (var i = 0; i < 3; i++) live.Step();
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == live.StateHash, "The minefield state round-trips through a snapshot.");
        var restoredFeeder = liveFeeder.ForkTo(restored);
        liveFeeder.GrantUpgrade(1, "Upgrade_EMPMines", target: 2);
        restoredFeeder.GrantUpgrade(1, "Upgrade_EMPMines", target: 2);
        for (var i = 0; i < live.Frames(6); i++)
            Check.That(live.Step().StateHash == restored.Step().StateHash, "The upgrade swap after a restore is hash-identical to the live run at step " + i + ".");
        Check.That(CountOf(restored, "EmpMine") == 38 && CountOf(restored, "TestMine") == 0, "The restored world swapped its mines.");
    }

    /// <summary>A snapshot taken inside <c>BaseRegenDelay</c> must resume with the same wake tick (the module persists it itself).</summary>
    public static void MatchBaseRegenerateSnapshot()
    {
        var files = new Dictionary<string, string>(MinefieldFiles())
        {
            ["Data/INI/Object/Regen.ini"] = """
            Object Regenerator
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 1000.0
                InitialHealth = 1000.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = BaseRegenerateUpdate Regen01
              End
            End
            """,
        };
        var fixture = new SyntheticFixture(TestRate.Default, files: files);
        var live = NewWorld(fixture);
        var feeder = new Feeder(live);
        feeder.Spawn(1, "Regenerator");
        live.Step();
        feeder.Damage(1, 300);
        for (var i = 0; i < live.Frames(10); i++) live.Step();
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == live.StateHash, "The delayed structure round-trips.");
        var body = restored.FindEntity(new EntityId(1))!.Body!;
        var damaged = body.Health;
        for (var i = 0; i < live.Frames(60); i++)
            Check.That(live.Step().StateHash == restored.Step().StateHash, "Regeneration after a restore is hash-identical at step " + i + ".");
        Check.That(body.Health > damaged, "The restored structure healed once the delay elapsed.");
    }
}

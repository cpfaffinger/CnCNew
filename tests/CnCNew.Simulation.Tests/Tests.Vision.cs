using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> VisionFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/VisionFixture.ini"] = """
        Weapon Snipe
          PrimaryDamage = 1
          AttackRange = 200.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End

        Object Scout
          KindOf = SELECTABLE INFANTRY
          VisionRange = 50.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY Snipe
          End
        End
        Object Watcher
          KindOf = SELECTABLE INFANTRY
          VisionRange = 10.0
          ShroudClearingRange = 100.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Blind
          KindOf = SELECTABLE INFANTRY
          VisionRange = 80.0
          ShroudClearingRange = 0.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Target
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Tower
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          GeometryIsSmall = Yes
          VisionRange = 120.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Beacon
          KindOf = STRUCTURE REVEAL_TO_ALL
          VisionRange = 30.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, TerrainMap Terrain) VisionSetup()
    {
        var fixture = new SyntheticFixture(SimulationRate.Original, files: VisionFiles());
        var terrain = NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        return (world, new Feeder(world), fixture, terrain);
    }

    private static ObjectTemplate VisionTemplate(SyntheticFixture fixture, string name)
        => fixture.Templates.TryGet(name, out var template) ? template : throw new InvalidOperationException(name);

    private static void VisionSettle(SimulationWorld world)
    {
        for (var tick = 0; tick < world.VisionInterval; tick++) world.Step();
    }

    private static Entity VisionAt(SimulationWorld world, string template, int x, int y)
        => world.Entities.Single(value => value.Template.Name == template && value.X == Fixed.FromInt(x) && value.Y == Fixed.FromInt(y));

    public static void VisionRevealsACircleAndKeepsExploration()
    {
        var (world, feeder, fixture, _) = VisionSetup();
        Check.That(VisionTemplate(fixture, "Scout").ShroudClearingRange == Fixed.FromInt(50), "The shroud clearing range defaults to the vision range.");
        Check.That(VisionTemplate(fixture, "Watcher").ShroudClearingRange == Fixed.FromInt(100) && VisionTemplate(fixture, "Blind").ShroudClearingRange == Fixed.Zero,
            "An explicit ShroudClearingRange wins, including zero.");
        feeder.Spawn(1, "Scout", x: 100, y: 100);
        feeder.Spawn(1, "Blind", x: 30, y: 30);
        VisionSettle(world);
        var vision = world.Vision!;
        Check.That(vision.IsVisible(1, 10, 10) && vision.IsVisible(1, 10, 14) && vision.IsVisible(1, 14, 10), "Cells within the range of the scout are visible to its owner.");
        Check.That(!vision.IsVisible(1, 10, 15) && !vision.IsVisible(1, 15, 15), "Cells beyond the radius (55, 78 units) and the diagonal corner stay dark.");
        Check.That(!vision.IsVisible(1, 3, 3) && !vision.IsExplored(1, 3, 3), "A looker with a zero shroud clearing range reveals nothing.");
        Check.That(!vision.IsVisible(2, 10, 10) && !vision.IsExplored(2, 10, 10), "An enemy does not share the sight.");
        Check.That(world.IsPositionVisibleTo(new PlayerId(1), Fixed.FromInt(103), Fixed.FromInt(103)) && !world.IsPositionVisibleTo(new PlayerId(2), Fixed.FromInt(103), Fixed.FromInt(103)),
            "The position queries use the cell under the point.");
        Check.That(world.IsPositionVisibleTo(PlayerId.System, Fixed.FromInt(190), Fixed.FromInt(190)), "The system authority sees everything.");

        var scout = VisionAt(world, "Scout", 100, 100);
        feeder.Kill(scout.Id.Value);
        VisionSettle(world);
        Check.That(!vision.IsVisible(1, 10, 10) && vision.IsExplored(1, 10, 10) && vision.IsExplored(1, 10, 14),
            "When the looker dies the cells go dark but stay explored.");
    }

    public static void VisionAlliesSharingAndConstruction()
    {
        var (world, feeder, _, _) = VisionSetup();
        feeder.Spawn(1, "Scout", x: 100, y: 100);
        feeder.Spawn(2, "Target", x: 180, y: 100);
        VisionSettle(world);
        Check.That(!world.Vision!.IsVisible(2, 10, 10), "Before an alliance player 2 does not see player 1's area.");
        feeder.SetRelationship(1, 2, Relationship.Allies);
        feeder.SetRelationship(2, 1, Relationship.Allies);
        VisionSettle(world);
        Check.That(world.Vision.IsVisible(2, 10, 10) && world.Vision.IsVisible(1, 10, 10), "Allies see through each other's objects.");

        var (site, siteFeeder, _, _) = VisionSetup();
        siteFeeder.Spawn(1, "Tower", x: 100, y: 100);
        siteFeeder.Spawn(1, "Scout", x: 20, y: 20);
        site.Step();
        var tower = VisionAt(site, "Tower", 100, 100);
        site.SetStatus(tower.Id, 1UL << ObjectStatusBits.UnderConstruction);
        VisionSettle(site);
        Check.That(site.Vision!.IsVisible(1, 10, 10) && !site.Vision.IsVisible(1, 10, 15) && !site.Vision.IsVisible(1, 10, 13),
            "A structure under construction only reveals its bounding circle (20 units), not its vision range.");
        site.SetStatus(tower.Id, 0, 1UL << ObjectStatusBits.UnderConstruction);
        VisionSettle(site);
        Check.That(site.Vision.IsVisible(1, 10, 19) && site.Vision.IsVisible(1, 10, 11), "A finished tower reveals its full vision range.");

        var (beacon, beaconFeeder, _, _) = VisionSetup();
        beaconFeeder.Spawn(1, "Beacon", x: 150, y: 150);
        VisionSettle(beacon);
        Check.That(beacon.Vision!.IsVisible(2, 15, 15) && beacon.Vision.IsVisible(1, 15, 15), "REVEAL_TO_ALL shows the area to every player.");
    }

    public static void VisionGatesTargeting()
    {
        var (world, feeder, _, _) = VisionSetup();
        feeder.Spawn(1, "Scout", x: 50, y: 50);
        feeder.Spawn(2, "Target", x: 150, y: 50);
        VisionSettle(world);
        var scout = VisionAt(world, "Scout", 50, 50);
        var target = VisionAt(world, "Target", 150, 50);
        Check.That(!world.CanPlayerSee(new PlayerId(1), target) && world.CanPlayerSee(new PlayerId(2), target) && world.CanPlayerSee(new PlayerId(1), scout),
            "A distant enemy is hidden; own objects are always visible.");
        Check.That(world.EntitiesVisibleTo(new PlayerId(1)).Select(entity => entity.Id).SequenceEqual([scout.Id]), "The visible-entity view lists only what the player may see.");
        var shot = feeder.FireWeapon(1, scout.Id.Value, target.Id.Value);
        Check.That(!shot.Accepted && shot.Reason == CommandRejection.TargetNotVisible, "A shot at a shrouded target is rejected.");
        Check.That(world.RejectionCounts[(int)CommandRejection.TargetNotVisible] == 1, "The rejection is counted.");
        Check.That(feeder.FireWeapon(0, scout.Id.Value, target.Id.Value).Reason != CommandRejection.TargetNotVisible, "The system authority is not limited by the shroud.");

        feeder.Spawn(1, "Watcher", x: 120, y: 50);
        VisionSettle(world);
        Check.That(world.CanPlayerSee(new PlayerId(1), target), "A friendly watcher with a longer clearing range reveals the target.");
        Check.That(feeder.FireWeapon(1, scout.Id.Value, target.Id.Value).Accepted, "The shot at a visible target is accepted.");

        var stealthed = 1UL << ObjectStatusBits.Index("STEALTHED");
        world.SetStatus(target.Id, stealthed);
        world.Step();
        Check.That(!world.CanPlayerSee(new PlayerId(1), target) && world.CanPlayerSee(new PlayerId(2), target), "A stealthed enemy is hidden from the other side even in a visible cell.");
        Check.That(feeder.FireWeapon(1, scout.Id.Value, target.Id.Value).Reason == CommandRejection.TargetNotVisible, "A stealthed target cannot be shot.");
        world.SetStatus(target.Id, 1UL << ObjectStatusBits.Index("DETECTED"));
        Check.That(world.CanPlayerSee(new PlayerId(1), target), "A detected stealthed target can be seen again.");
    }

    public static void VisionSnapshotAndDeterminism()
    {
        var (first, firstFeeder, fixture, terrain) = VisionSetup();
        var (second, secondFeeder, _, _) = VisionSetup();
        foreach (var (world, feeder) in new[] { (first, firstFeeder), (second, secondFeeder) })
        {
            feeder.Spawn(1, "Scout", x: 60, y: 60);
            feeder.Spawn(2, "Tower", x: 150, y: 150);
        }
        for (var tick = 0; tick < 20; tick++)
            Check.That(first.Step().StateHash == second.Step().StateHash, "Two identical runs agree in every tick, vision included.");
        var hashBefore = first.StateHash;
        firstFeeder.Kill(VisionAt(first, "Scout", 60, 60).Id.Value);
        first.Step();
        Check.That(first.StateHash != hashBefore, "The state hash covers the vision.");
        for (var tick = 0; tick < 5; tick++) first.Step();

        var bytes = WorldSnapshot.Write(first);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain);
        Check.That(restored.Vision!.IsExplored(1, 6, 6) && !restored.Vision.IsVisible(1, 6, 6) && restored.Vision.IsVisible(2, 15, 15), "Explored and visible cells survive a restore.");
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "A snapshot with vision round-trips byte-identically.");
        for (var tick = 0; tick < 30; tick++) Check.That(first.Step().StateHash == restored.Step().StateHash, "A restored world continues hash-identically.");

        var plain = NewWorld(new SyntheticFixture(SimulationRate.Original));
        Check.That(plain.Vision is null && plain.IsPositionVisibleTo(new PlayerId(1), Fixed.FromInt(5), Fixed.FromInt(5)), "Without a terrain there is no fog: everything is visible.");
    }
}

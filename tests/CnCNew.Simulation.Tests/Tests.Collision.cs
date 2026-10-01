using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    // Speed etc. are per-second (SimulationTiming.LogicFramesPerSecond = 30 ticks/second): 300/sec = 10 world units/tick.
    private const string CollisionLoco = """
          Surfaces = GROUND
          TurnRate = 99999.0
          Speed = 300.0
          Acceleration = 30000.0
          Braking = 30000.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        """;

    /// <summary>
    /// Phase 10 (docs/HANDOFF-PHASE10.md part B point 9): <c>ToppleUpdate</c>, <c>StructureToppleUpdate</c>, <c>HeightDieUpdate</c>
    /// and real footprint collision for <c>CrushDie</c>/<c>SquishCollide</c>, plus the bounded local avoidance added alongside them.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CollisionFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/CollisionFixture.ini"] = $$"""
        Locomotor GroundLoco
        {{CollisionLoco}}
        End
        Weapon CrushWeapon
          PrimaryDamage = 10
          PrimaryDamageRadius = 15.0
          AttackRange = 999.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End
        Object Crusher
          KindOf = SELECTABLE VEHICLE
          CrusherLevel = 2
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object WeakCrusher
          KindOf = SELECTABLE VEHICLE
          CrusherLevel = 1
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object NonCrusher
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Blocker
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 3.0
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Infantry
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SquishCollide Squish01
          End
        End
        Object Stump
          Geometry = CYLINDER
          GeometryMajorRadius = 1.0
          GeometryMinorRadius = 1.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Tree
          Geometry = CYLINDER
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 3.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ToppleUpdate Topple01
            StumpName = Stump
            BounceVelocityPercent = 1%
          End
        End
        Object ImmediateTree
          Geometry = CYLINDER
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 3.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ToppleUpdate Topple01
            KillWhenStartToppling = Yes
          End
        End
        Object Building
          KindOf = STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 40.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = StructureToppleUpdate Topple01
            MinToppleDelay = 100
            MaxToppleDelay = 100
            StructuralIntegrity = 0.5
            StructuralDecay = 0.9
            CrushingWeaponName = CrushWeapon
          End
        End
        Object TallHider
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 60.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Faller
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = HeightDieUpdate Height01
            TargetHeight = 0.0
            TargetHeightIncludesStructures = Yes
          End
        End
        Object DelayedFaller
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = HeightDieUpdate Height01
            TargetHeight = 50.0
            InitialDelay = 100
          End
        End
        Object GuardedFaller
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = HeightDieUpdate Height01
            TargetHeight = 50.0
            OnlyWhenMovingDown = Yes
          End
        End
        Object GroundedFaller
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = HeightDieUpdate Height01
            TargetHeight = 100.0
          End
        End
        """,
    };

    private static readonly SyntheticFixture CollisionFixture = new(files: CollisionFiles());

    private static TerrainMap CollisionTerrain(int cells = 30, Func<int, int, int>? height = null)
        => TerrainMap.Build(SyntheticMaps.Build(cells, cells, 1, height ?? ((_, _) => 0)), "collision");

    private static (SimulationWorld World, Feeder Feeder) CollisionWorld(TerrainMap? terrain = null)
    {
        var map = terrain ?? CollisionTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 1, TwoPlayers, SyntheticFixture.FixtureId, CollisionFixture.Fingerprint, map.Fingerprint), CollisionFixture.Templates, map);
        return (world, new Feeder(world));
    }

    /// <summary><c>SquishCollide.cpp onCollide</c>, now wired to real footprint overlap (<c>SimulationWorld.Collision.cs</c>, <c>TrySquish</c>).</summary>
    public static void CollisionCrushAndSquish()
    {
        Check.That(CollisionFixture.Templates.TryGet("Infantry", out var infantry) && infantry.IsInstantiable, "SquishCollide stays instantiable.");

        // An enemy crusher closing on infantry crushes it once their footprints overlap.
        var (world, feeder) = CollisionWorld();
        feeder.Spawn(1, "Crusher", x: 0, y: 0);
        feeder.Spawn(2, "Infantry", x: 40, y: 0);
        world.Step();
        feeder.SetMoveTarget(1, 1, 100, 0);
        long died = -1;
        for (var tick = 0; tick < 20 && died < 0; tick++)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity.Value == 2)) died = world.Tick;
        }
        Check.That(died > 0, "The crusher squishes the infantry once it overlaps and is still closing on it.");
        Check.That(world.FindEntity(new EntityId(1)) is not null, "The crusher itself survives the squish.");

        // Allies never squish each other, even with a crusher level above zero.
        var (allyWorld, allyFeeder) = CollisionWorld();
        allyFeeder.Spawn(1, "Crusher", x: 0, y: 0);
        allyFeeder.Spawn(1, "Infantry", x: 40, y: 0);
        allyWorld.Step();
        allyFeeder.SetMoveTarget(1, 1, 100, 0);
        for (var tick = 0; tick < 20; tick++) allyWorld.Step();
        Check.That(allyWorld.FindEntity(new EntityId(2)) is not null, "An allied crusher does not squish its own infantry.");

        // CrusherLevel 0 cannot crush or squish anything.
        var (safeWorld, safeFeeder) = CollisionWorld();
        safeFeeder.Spawn(1, "NonCrusher", x: 0, y: 0);
        safeFeeder.Spawn(2, "Infantry", x: 40, y: 0);
        safeWorld.Step();
        safeFeeder.SetMoveTarget(1, 1, 100, 0);
        for (var tick = 0; tick < 20; tick++) safeWorld.Step();
        Check.That(safeWorld.FindEntity(new EntityId(2)) is not null, "CrusherLevel 0 squishes nobody.");

        // Two independent runs of the lethal scenario are hash-identical.
        var (runA, feederA) = CollisionWorld();
        feederA.Spawn(1, "Crusher", x: 0, y: 0);
        feederA.Spawn(2, "Infantry", x: 40, y: 0);
        runA.Step();
        feederA.SetMoveTarget(1, 1, 100, 0);
        var (runB, feederB) = CollisionWorld();
        feederB.Spawn(1, "Crusher", x: 0, y: 0);
        feederB.Spawn(2, "Infantry", x: 40, y: 0);
        runB.Step();
        feederB.SetMoveTarget(1, 1, 100, 0);
        for (var tick = 0; tick < 20; tick++) Check.That(runA.Step().StateHash == runB.Step().StateHash, "Crush resolution is deterministic at tick " + tick + ".");
    }

    /// <summary><c>ToppleUpdate.cpp onCollide</c>/<c>applyTopplingForce</c>, now driven by <c>TryTopple</c>.</summary>
    public static void CollisionTopple()
    {
        var (world, feeder) = CollisionWorld();
        feeder.Spawn(1, "Crusher", x: 0, y: 0);
        feeder.Spawn(2, "Tree", x: 40, y: 0);
        world.Step();
        feeder.SetMoveTarget(1, 1, 100, 0);
        long died = -1;
        for (var tick = 0; tick < 300 && died < 0; tick++)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity.Value == 2)) died = world.Tick;
        }
        Check.That(died > 0, "A crusher above level 1 eventually topples and kills the tree.");
        Check.That(world.Entities.Any(value => value.Template.Name == "Stump"), "The tree spawns its stump when it starts toppling.");

        // CrusherLevel 1 squishes infantry but cannot topple a tree (reference: getCrusherLevel() > 1).
        var (weak, weakFeeder) = CollisionWorld();
        weakFeeder.Spawn(1, "WeakCrusher", x: 0, y: 0);
        weakFeeder.Spawn(2, "Tree", x: 40, y: 0);
        weak.Step();
        weakFeeder.SetMoveTarget(1, 1, 100, 0);
        for (var tick = 0; tick < 300; tick++) weak.Step();
        Check.That(weak.FindEntity(new EntityId(2)) is not null, "CrusherLevel 1 does not topple trees.");

        // KillWhenStartToppling dies the instant the push lands, no fall animation to wait out.
        var (instant, instantFeeder) = CollisionWorld();
        instantFeeder.Spawn(1, "Crusher", x: 0, y: 0);
        instantFeeder.Spawn(2, "ImmediateTree", x: 40, y: 0);
        instant.Step();
        instantFeeder.SetMoveTarget(1, 1, 100, 0);
        long instantDied = -1;
        for (var tick = 0; tick < 20 && instantDied < 0; tick++)
        {
            var result = instant.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity.Value == 2)) instantDied = instant.Tick;
        }
        Check.That(instantDied > 0 && instantDied < died, "KillWhenStartToppling dies well before the full fall (" + instantDied + " vs " + died + ").");

        // A mid-fall snapshot restores and keeps toppling to the same outcome (round-trips through Describe/Load).
        var (live, liveFeeder) = CollisionWorld();
        liveFeeder.Spawn(1, "Crusher", x: 0, y: 0);
        liveFeeder.Spawn(2, "Tree", x: 40, y: 0);
        live.Step();
        liveFeeder.SetMoveTarget(1, 1, 100, 0);
        for (var tick = 0; tick < 5; tick++) live.Step();
        Check.That(live.FindEntity(new EntityId(2)) is not null, "Setup: the tree is still falling (mid-bounce) at the snapshot point.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), CollisionFixture.Templates, CollisionFixture.Fingerprint, CollisionTerrain());
        Check.That(restored.StateHash == live.StateHash, "The mid-fall topple state round-trips through a snapshot.");
        for (var tick = 0; tick < 20; tick++)
            Check.That(live.Step().StateHash == restored.Step().StateHash, "The restored tree keeps toppling identically at step " + tick + ".");
        Check.That(live.FindEntity(new EntityId(2)) is null && restored.FindEntity(new EntityId(2)) is null, "Both the live and the restored run finish the topple with the same outcome.");
    }

    /// <summary><c>StructureToppleUpdate.cpp</c>: delayed collapse and the ground-damage sweep (reduced to the leading edge, see the module's doc comment).</summary>
    public static void CollisionStructureTopple()
    {
        var (world, feeder) = CollisionWorld();
        feeder.Spawn(1, "Building", x: 0, y: 0);
        world.Step();
        feeder.Damage(1, 1000);
        var events = new List<SimEvent>();
        for (var tick = 0; tick < 400; tick++) events.AddRange(world.Step().Events);
        Check.That(world.FindEntity(new EntityId(1)) is not null, "The building persists as rubble: StructureToppleUpdate is itself the only die module, and it never requests destruction.");
        // The crushing weapon only fires once the building has fallen close to flat (theta <= PI/6), so the skip fires at least once
        // once MinToppleDelay plus most of the fall has elapsed; a victim's exact position would depend on the random topple
        // direction (LogicRandom jitter), so this checks that the code path ran, not that it hit anyone.
        Check.That(events.Count(value => value.Kind == SimEventKind.CreationSkipped && value.Value == CreationSkipReasons.SettingNotSimulated && value.Text.Contains("grid sweep")) == 1,
            "The reduced crushing-damage sweep reports itself exactly once: " + events.Count(value => value.Kind == SimEventKind.CreationSkipped));

        // Two independent runs of the collapse (random delay and direction) are hash-identical.
        var (runA, feederA) = CollisionWorld();
        feederA.Spawn(1, "Building", x: 0, y: 0);
        runA.Step();
        feederA.Damage(1, 1000);
        var (runB, feederB) = CollisionWorld();
        feederB.Spawn(1, "Building", x: 0, y: 0);
        runB.Step();
        feederB.Damage(1, 1000);
        for (var tick = 0; tick < 400; tick++) Check.That(runA.Step().StateHash == runB.Step().StateHash, "The structure's collapse is deterministic at tick " + tick + ".");
    }

    /// <summary><c>HeightDieUpdate.cpp</c>: height-threshold death, the initial delay, the structure-height escalation and the "never below ground" floor.</summary>
    public static void CollisionHeightDie()
    {
        // Baseline: flat terrain, TargetHeight 0 means "at or above ground is safe" (Z starts at 0, never below the target).
        var (world, feeder) = CollisionWorld();
        feeder.Spawn(1, "Faller", x: 0, y: 0);
        world.Step();
        for (var tick = 0; tick < 10; tick++) world.Step();
        Check.That(world.FindEntity(new EntityId(1)) is not null, "On open ground the object stays above its target height.");

        // A tall structure nearby raises the target height past the object's position, so it now dies.
        // The structure scan range is the faller's own (small) bounding circle, just like the reference: the tall object must sit almost on top of it.
        feeder.Spawn(2, "TallHider", x: 1, y: 0);
        long died = -1;
        for (var tick = 0; tick < 10 && died < 0; tick++)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity.Value == 1)) died = world.Tick;
        }
        Check.That(died > 0, "TargetHeightIncludesStructures raises the threshold above a nearby tall structure.");

        // InitialDelay postpones the check.
        var (delayed, delayedFeeder) = CollisionWorld();
        delayedFeeder.Spawn(1, "DelayedFaller", x: 0, y: 0);
        delayed.Step();
        Check.That(delayed.FindEntity(new EntityId(1)) is not null, "InitialDelay keeps the object alive before it elapses.");
        for (var tick = 0; tick < 10; tick++) delayed.Step();
        Check.That(delayed.FindEntity(new EntityId(1)) is null, "The object dies once InitialDelay elapses and it is still below the target height.");

        // OnlyWhenMovingDown: the object's height never decreases here (nothing moves it), so the gate blocks death forever.
        var (guarded, guardedFeeder) = CollisionWorld();
        guardedFeeder.Spawn(1, "GuardedFaller", x: 0, y: 0);
        guarded.Step();
        for (var tick = 0; tick < 20; tick++) guarded.Step();
        Check.That(guarded.FindEntity(new EntityId(1)) is not null, "OnlyWhenMovingDown blocks death while Z is constant.");

        // The object never ends up below the terrain, even when the whole map is a plateau well above world Z 0.
        var (grounded, groundedFeeder) = CollisionWorld(terrain: CollisionTerrain(height: (_, _) => 40));
        groundedFeeder.Spawn(1, "GroundedFaller", x: 0, y: 0);
        grounded.Step();
        long groundDied = -1;
        for (var tick = 0; tick < 5 && groundDied < 0; tick++)
        {
            var result = grounded.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity.Value == 1)) groundDied = grounded.Tick;
        }
        Check.That(groundDied > 0, "GroundedFaller is well below its target height on the plateau and dies.");

        // Snapshot round trip.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(delayed), CollisionFixture.Templates, CollisionFixture.Fingerprint, CollisionTerrain());
        Check.That(restored.StateHash == delayed.StateHash, "HeightDieUpdate's hasDied/earliestDeathTick/lastZ state round-trips through a snapshot.");
    }

    /// <summary>
    /// Local collision avoidance (<c>SimulationWorld.Collision.cs</c>, <c>AvoidLocalBlockers</c>): a bounded nudge-and-retry around another
    /// unit's footprint, not steering. Documented limit: it looks one step ahead and does not coordinate with the blocker.
    /// </summary>
    public static void CollisionLocalAvoidance()
    {
        var (world, feeder) = CollisionWorld();
        feeder.Spawn(1, "NonCrusher", x: 0, y: 0);
        feeder.Spawn(2, "Blocker", x: 50, y: 0);
        world.Step();
        feeder.SetMoveTarget(1, 1, 100, 0);
        var deviated = false;
        var arrived = false;
        for (var tick = 0; tick < 60 && !arrived; tick++)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.MoveArrived && value.Entity.Value == 1)) arrived = true;
            var mover = world.FindEntity(new EntityId(1));
            if (mover is not null && mover.Y != Fixed.Zero) deviated = true;
        }
        Check.That(deviated, "The mover steps around the blocker instead of driving straight through it.");
        Check.That(arrived, "Despite the detour, the mover still reaches its destination within a bounded number of ticks.");
        Check.That(world.FindEntity(new EntityId(2)) is not null, "The stationary blocker (no crusher level) is untouched.");

        // Determinism: two independent runs of the same avoidance scenario agree exactly.
        var (runA, feederA) = CollisionWorld();
        feederA.Spawn(1, "NonCrusher", x: 0, y: 0);
        feederA.Spawn(2, "Blocker", x: 50, y: 0);
        runA.Step();
        feederA.SetMoveTarget(1, 1, 100, 0);
        var (runB, feederB) = CollisionWorld();
        feederB.Spawn(1, "NonCrusher", x: 0, y: 0);
        feederB.Spawn(2, "Blocker", x: 50, y: 0);
        runB.Step();
        feederB.SetMoveTarget(1, 1, 100, 0);
        for (var tick = 0; tick < 60; tick++) Check.That(runA.Step().StateHash == runB.Step().StateHash, "Avoidance is deterministic at tick " + tick + ".");
    }
}

using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// ActiveBody::setCorrectDamageState: a structure whose body is rubble collides with nothing, and its cells are CELL_RUBBLE, which only a locomotor with the surface RUBBLE enters.
static partial class Tests
{
    private static Fixed RubbleFixed(int value) => Fixed.FromInt(value);

    public static void RubbleIsOnlyForItsSurface()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: AttackViewFiles());
        fixture.Templates.TryGet("ViewWall", out var wall);
        var grid = new NavigationGrid(TerrainMap.Build(SyntheticMaps.Build(10, 10, 1), "flat"));
        const LocomotorSurfaces infantry = LocomotorSurfaces.Ground | LocomotorSurfaces.Rubble;
        var (x, y) = (RubbleFixed(50), RubbleFixed(55));

        grid.AddFootprint(7, wall!, x, y, Fixed.Zero);
        var whole = grid.ObstacleSum;
        Check.That(!grid.IsPassable(5, 5, infantry) && !grid.IsPassable(5, 5, LocomotorSurfaces.Ground), "A whole structure stops everybody.");
        Check.That(grid.IsAttackLineBlocked(RubbleFixed(5), y, RubbleFixed(95), y, []), "And blocks the view.");

        grid.RemoveFootprint(7, wall!, x, y, Fixed.Zero);
        grid.AddRubble(7, wall!, x, y, Fixed.Zero);
        Check.That(grid.ObstacleSum != whole, "A path of before knows the grid has changed.");
        Check.That(grid.IsPassable(5, 5, infantry), "A locomotor with the surface RUBBLE enters the rubble.");
        Check.That(!grid.IsPassable(5, 5, LocomotorSurfaces.Ground), "Another does not (validLocomotorSurfacesForCellType).");
        Check.That(!grid.IsBlockedByObstacle(5, 5), "Rubble is no obstacle.");
        Check.That(!grid.IsAttackLineBlocked(RubbleFixed(5), y, RubbleFixed(95), y, []), "Rubble blocks no view.");

        int Length(LocomotorSurfaces surfaces) => grid.FindPath(RubbleFixed(5), y, RubbleFixed(95), y, surfaces).Points.Length;
        Check.That(grid.FindPath(RubbleFixed(5), y, RubbleFixed(95), y, infantry).Status == PathStatus.Found, "A path across it is found.");
        Check.That(Length(LocomotorSurfaces.Ground) >= Length(infantry), "The other way round it is no shorter.");

        // A mover caught on the rubble walks out of it, as out of a footprint.
        var caught = grid.FindPath(x, y, RubbleFixed(95), y, LocomotorSurfaces.Ground);
        Check.That(caught.Status == PathStatus.Found, "A mover that stands on rubble it could not enter walks out.");

        grid.RemoveRubble(7, wall!, x, y, Fixed.Zero);
        Check.That(grid.IsPassable(5, 5, LocomotorSurfaces.Ground), "When the object goes, the cell is clear.");
    }

    public static void AirborneTargetIsTheHeightOfTheLocomotor()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: AttackGateFiles());
        var terrain = TerrainMap.Build(SyntheticMaps.Build(30, 30, 1), "airborne");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(2, "HoverCraft", owner: 2, x: 100, y: 150, z: 20);
        feeder.Spawn(2, "HoverCraft", owner: 2, x: 150, y: 150, z: 40);
        feeder.Spawn(1, "LosSoldier", owner: 1, x: 200, y: 150, z: 40);
        world.Step();
        var low = world.Entities.First(value => value.Template.Name == "HoverCraft" && value.X < Fixed.FromInt(120));
        var high = world.Entities.First(value => value.Template.Name == "HoverCraft" && value.X > Fixed.FromInt(120));
        var thrown = world.Entities.First(value => value.Template.Name == "LosSoldier");
        Check.That(world.IsSignificantlyAboveTerrain(low) && !world.IsAirborneTarget(low), $"Below AirborneTargetingHeight a helicopter is significantly above the terrain and no airborne target ({low.Z}, {world.IsSignificantlyAboveTerrain(low)}, {world.IsAirborneTarget(low)}).");
        Check.That(world.IsAirborneTarget(high), "Above it, it is one.");
        Check.That(world.IsSignificantlyAboveTerrain(thrown) && !world.IsAirborneTarget(thrown), "A locomotor that does not name the height makes no airborne target, however high.");
    }

    /// <summary>A plane that drops a bomb flying on or standing; returns where the bomb hits the ground and the heading it left with.</summary>
    private static (Fixed Flight, Fixed Angle) BombDrop(bool keepsVelocity)
    {
        var files = new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/BombFixture.ini"] = $"""
            Locomotor BomberLoco
              Surfaces = AIR
              AirborneTargetingHeight = 30
              Speed = 90.0
              Acceleration = 900.0
              Braking = 900.0
              TurnRate = 360.0
              MinSpeed = 0.0
              CloseEnoughDist = 1.0
              PreferredHeight = 100.0
            End

            Object TestBomb
              KindOf = PROJECTILE
              TransportSlotCount = 1
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = PhysicsBehavior Physics01
                Mass = 10.0
                CenterOfMassOffset = 2.0
              End
            End

            Object TestBomber
              KindOf = SELECTABLE VEHICLE TRANSPORT AIRCRAFT
              Locomotor = SET_NORMAL BomberLoco
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = PhysicsBehavior Physics01
                Mass = 100.0
              End
              Behavior = DeliverPayloadAIUpdate AI01
                MaxAttempts = 1
                DoorDelay = 34
                DropDelay = 100
                DeliveryDistance = 80.0
              End
              Behavior = TransportContain Contain01
                Slots = 10
                AllowInsideKindOf = PROJECTILE
                OrientLikeContainerOnExit = {(keepsVelocity ? "Yes" : "No")}
                KeepContainerVelocityOnExit = {(keepsVelocity ? "Yes" : "No")}
                ExitPitchRate = 30
              End
            End
            """,
        };
        var fixture = new SyntheticFixture(TestRate.Default, files: files);
        var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "bomb");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "TestBomber", owner: 1, x: 100, y: 300);
        feeder.Spawn(1, "TestBomb", owner: 1, x: 100, y: 300);
        world.Step();
        var plane = world.Entities.First(value => value.Template.Name == "TestBomber");
        var bomb = world.Entities.First(value => value.Template.Name == "TestBomb");
        Check.That(world.TryEnterContainer(bomb, plane), "The bomb is aboard.");
        var deliver = plane.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.DeliverPayloadAIUpdate>().First();
        deliver.DeliverViaModuleData(world, Fixed.FromInt(500), Fixed.FromInt(300));
        var limit = world.Frames(900);
        for (var step = 0; step < limit && bomb.ContainedBy.IsValid; step++) world.Step();
        Check.That(!bomb.ContainedBy.IsValid, "The bomb has left.");
        var (from, angle) = (bomb.X, bomb.Angle);
        for (var step = 0; step < limit && bomb.Z > Fixed.FromInt(1); step++) world.Step();
        return (bomb.X - from, angle);
    }

    public static void BombFliesOnFromItsPlane()
    {
        var (still, _) = BombDrop(false);
        var (carried, angle) = BombDrop(true);
        Check.That(still.Abs() < Fixed.One, $"A bomb that keeps nothing falls straight down ({still}).");
        Check.That(carried > Fixed.FromInt(50), $"One that keeps the plane's velocity lands ahead of the place it left ({carried}).");
        Check.That(angle.Abs() < Fixed.FromDecimal(0.2m), $"It leaves facing as the plane does ({angle}).");
    }

    public static void IdleWeaponFillsItsClip()
    {
        var files = new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/ReloadFixture.ini"] = """
            Weapon ReloadGun
              PrimaryDamage = 1
              AttackRange = 500.0
              DamageType = EXPLOSION
              ClipSize = 3
              ClipReloadTime = 600000
              DelayBetweenShots = 100
              AutoReloadWhenIdle = 2000
              AntiGround = Yes
            End

            Object ReloadShooter
              KindOf = SELECTABLE VEHICLE
              Body = ActiveBody Body01
                MaxHealth = 1000.0
                InitialHealth = 1000.0
              End
              WeaponSet
                Conditions = None
                Weapon = PRIMARY ReloadGun
              End
            End

            Object ReloadDummy
              KindOf = SELECTABLE VEHICLE
              Body = ActiveBody Body01
                MaxHealth = 100000.0
                InitialHealth = 100000.0
              End
            End
            """,
        };
        var fixture = new SyntheticFixture(TestRate.Default, files: files);
        var terrain = TerrainMap.Build(SyntheticMaps.Build(30, 30, 1), "reload");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "ReloadShooter", owner: 1, x: 100, y: 150);
        feeder.Spawn(2, "ReloadDummy", owner: 2, x: 150, y: 150);
        world.Step();
        var shooter = world.Entities.First(value => value.Template.Name == "ReloadShooter");
        var dummy = world.Entities.First(value => value.Template.Name == "ReloadDummy");
        Check.That(world.TryFireWeaponSlot(shooter, 0, dummy), "It fires.");
        for (var step = 0; step < world.Frames(30); step++) world.Step();
        Check.That(shooter.ClipOf(0) == 2, $"One shot is gone ({shooter.ClipOf(0)}).");
        Check.That(world.TryFireWeaponSlot(shooter, 0, dummy), "It fires again, which pushes the reload back.");
        for (var step = 0; step < world.Frames(50); step++) world.Step();
        Check.That(shooter.ClipOf(0) == 1, $"The clip is not full yet ({shooter.ClipOf(0)}).");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
        for (var step = 0; step < world.Frames(20); step++) { world.Step(); restored.Step(); }
        Check.That(shooter.ClipOf(0) == 3, $"Two seconds after the last shot the clip is full ({shooter.ClipOf(0)}).");
        Check.That(restored.StateHash == world.StateHash, "A restored world reloads at the same tick.");
    }

    public static void OverkillWeighsADeath()
    {
        // ProbabilityModifier 5, ModifierBonusPerOverkillPercent 20 %: the bonus is a share of the health cut to a whole number.
        Check.That(SlowDeathLottery.Weight(5, Fixed.FromDecimal(0.2m), Fixed.Zero) == 5, "A death without overkill weighs its modifier.");
        Check.That(SlowDeathLottery.Weight(5, Fixed.FromDecimal(0.2m), Fixed.FromDecimal(4.9m)) == 5, "Short of five times the health the bonus is cut away.");
        Check.That(SlowDeathLottery.Weight(5, Fixed.FromDecimal(0.2m), Fixed.FromInt(11)) == 7, "Eleven times the health adds two.");
        Check.That(SlowDeathLottery.Weight(0, Fixed.Zero, Fixed.Zero) == 1, "Nothing weighs less than one.");
    }

    private static IReadOnlyDictionary<string, string> WheelFiles(string pivot) => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/WheelFixture.ini"] = $"""
        Locomotor CarLoco
          Surfaces = GROUND
          Appearance = FOUR_WHEELS
          Speed = 60.0
          MinTurnSpeed = 15.0
          Acceleration = 60.0
          Braking = 120.0
          TurnRate = 180.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
          TurnPivotOffset = {pivot}
        End

        Object TestCar
          KindOf = SELECTABLE VEHICLE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 5.0
          Locomotor = SET_NORMAL CarLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>A car at rest told to go to a place at its side: the angle it has turned after half a second, and the way it stands from its start then and when it has arrived.</summary>
    private static (Fixed Turned, bool Arrived, Fixed DriftAtStart) CarTurn(string pivot)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: WheelFiles(pivot));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "wheels");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "TestCar", owner: 1, x: 300, y: 300);
        world.Step();
        var car = world.Entities.First(value => value.Template.Name == "TestCar");
        var (startX, startAngle) = (car.X, car.Angle);
        Check.That(feeder.SetMoveTarget(1, car.Id.Value, 300, 500).Accepted, "The car takes the order.");
        for (var step = 0; step < world.Frames(3); step++) world.Step();
        var early = car.Angle;
        for (var step = 0; step < world.Frames(900) && car.HasMoveTarget; step++) world.Step();
        return (FixedTrig.AngleDifference(startAngle, early).Abs(), !car.HasMoveTarget, (car.X - startX).Abs());
    }

    public static void WheelsTurnOnlyWhileTheyMove()
    {
        var (turned, arrived, _) = CarTurn("0.0");
        Check.That(turned < Fixed.FromDecimal(0.05m), $"A car at rest does not turn on the spot ({turned}).");
        Check.That(arrived, "It gets where it was sent.");
        var (_, pivoted, _) = CarTurn("-0.5");
        Check.That(pivoted, "With its pivot at the rear as well.");

        // A goal far behind a unit that can reverse: it backs up with its front swinging to the goal, and drives off forwards (DOING_THREE_POINT_TURN).
        var fixture = new SyntheticFixture(TestRate.Default, files: WheelFiles("0.0").ToDictionary(pair => pair.Key, pair => pair.Value.Replace("Appearance = FOUR_WHEELS", "Appearance = FOUR_WHEELS\n  CanMoveBackwards = Yes")));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "three-point");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "TestCar", owner: 1, x: 300, y: 300);
        world.Step();
        var car = world.Entities.First(value => value.Template.Name == "TestCar");
        var (startX, startAngle) = (car.X, car.Angle);
        Check.That(feeder.SetMoveTarget(1, car.Id.Value, 80, 300).Accepted, "It takes the order.");
        var (backedFacing, swung) = (Fixed.Zero, Fixed.Zero);
        var backed = false;
        for (var step = 0; step < world.Frames(900) && (step < 2 || car.HasMoveTarget); step++)
        {
            world.Step();
            swung = Fixed.Max(swung, FixedTrig.AngleDifference(startAngle, car.Angle).Abs());
            if (backed || car.X > startX - Fixed.FromInt(2)) continue;
            backed = true;
            backedFacing = swung;
        }
        Check.That(backed && backedFacing < Fixed.FromDecimal(0.5m), $"It sets off backwards, its tail to the goal ({backedFacing}).");
        Check.That(swung > FixedTrig.HalfPi, $"Its front swings round ({swung}).");
        Check.That(!car.HasMoveTarget, "It arrives.");
    }

    public static void StructureThatDiesBecomesRubble()
    {
        var files = new Dictionary<string, string>(AttackViewFiles())
        {
            ["Data/INI/Object/RubbleFixture.ini"] = """
            Object RubbleWall
              KindOf = STRUCTURE
              GeometryIsSmall = No
              Geometry = CYLINDER
              GeometryMajorRadius = 15.0
              GeometryHeight = 20.0
              Body = StructureBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = KeepObjectDie Die01
              End
            End
            """,
        };
        var fixture = new SyntheticFixture(TestRate.Default, files: files);
        var terrain = TerrainMap.Build(SyntheticMaps.Build(30, 30, 1), "rubble");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        new Feeder(world).Spawn(1, "RubbleWall", owner: 1, x: 150, y: 150);
        for (var step = 0; step < world.Frames(3); step++) world.Step();
        var wall = world.Entities.First(value => value.Template.Name == "RubbleWall");
        const LocomotorSurfaces infantry = LocomotorSurfaces.Ground | LocomotorSurfaces.Rubble;
        Check.That(!wall.IsRubble && !world.Navigation!.IsPassable(15, 15, infantry), "A standing structure is no rubble.");

        var noCollisions = ObjectStatusBits.Index("NO_COLLISIONS");
        Check.That(!wall.HasStatus(noCollisions), "And collides.");
        world.Damage(wall.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(1000), DamageTypes.Unresistable, DeathTypes.Normal, true));
        for (var step = 0; step < world.Frames(3); step++) world.Step();

        Check.That(world.FindEntity(wall.Id) is { EffectivelyDead: true, IsRubble: true }, "The dead structure stays, as rubble.");
        Check.That(wall.HasStatus(noCollisions), "\"Here we make sure nobody collides with us, ever again.\"");
        Check.That(world.Navigation!.IsPassable(15, 15, infantry) && !world.Navigation.IsPassable(15, 15, LocomotorSurfaces.Ground), "Its cells are rubble.");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
        Check.That(restored.Navigation!.IsPassable(15, 15, infantry) && !restored.Navigation.IsPassable(15, 15, LocomotorSurfaces.Ground), "A restored world has the rubble on its grid.");
        Check.That(restored.Navigation.ObstacleSum == world.Navigation.ObstacleSum, "And the same set of footprints.");
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(WorldSnapshot.Write(world)), "The rubble survives a snapshot.");
    }
}

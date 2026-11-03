using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static readonly string UnmannedIni = """
        Locomotor PilotLoco
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 90
        End
        Weapon PilotBomb
          PrimaryDamage = 1.0
          AttackRange = 10.0
          DamageType = EXPLOSION
          DeathType = NORMAL
          DelayBetweenShots = 100
        End
        Object Jeep
          KindOf = VEHICLE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL PilotLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Technical
          KindOf = VEHICLE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL PilotLoco
          WeaponSet
            Conditions = CARBOMB
            Weapon = PRIMARY PilotBomb
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Grunt
          KindOf = INFANTRY SELECTABLE
          VisionRange = 300.0
          ShroudClearingRange = 300.0
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL PilotLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Hero
          KindOf = INFANTRY SELECTABLE REJECT_UNMANNED
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL PilotLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    private static DamageInfo PilotShot(EntityId source) => new(source, Fixed.One, DamageTypes.KillPilot, DeathTypes.Normal, false);

    /// <summary>
    /// <c>ActiveBody::attemptDamage</c>, <c>Object::setDisabled</c>, <c>PhysicsBehavior::onCollide</c>: a shot at the driver leaves the vehicle whole, standing and nobody's; any infantry but a hero that walks
    /// into it makes it its player's and is gone; a vehicle with a car bomb set blows up instead.
    /// </summary>
    public static void AVehicleLosesItsDriver()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var files = new Dictionary<string, string>(GroupMoveFiles()) { ["Data/INI/Object/Unmanned.ini"] = UnmannedIni };
            var (world, feeder, _) = RatedWorld(rate, files, NavTerrain());
            feeder.Spawn(1, "Jeep", x: 100, y: 100);
            feeder.Spawn(2, "Grunt", x: 60, y: 100);
            feeder.Spawn(2, "Hero", x: 60, y: 120);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var jeep = world.Entities.Single(value => value.Template.Name == "Jeep");
            var grunt = world.Entities.Single(value => value.Template.Name == "Grunt");
            var hero = world.Entities.Single(value => value.Template.Name == "Hero");
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), 1000, world.Tick, jeep.Id, Fixed.FromInt(170), Fixed.FromInt(100))).Accepted, $"{rate}: the move is accepted.");
            for (var step = 0; step < world.Frames(10); step++) world.Step();
            world.Damage(jeep.Id, PilotShot(grunt.Id));
            Check.That(jeep.Unmanned && jeep.Owner == PlayerId.System, $"{rate}: the vehicle lost its driver and belongs to nobody.");
            Check.That(jeep.Body!.Health == jeep.Body.MaxHealth, $"{rate}: the shot does not damage the vehicle.");
            Check.That((jeep.DisabledFlags(world.Tick) & DisabledMask.Unmanned) != 0, $"{rate}: it is disabled.");
            Check.That(!jeep.HasMoveTarget, $"{rate}: it stops.");

            Check.That(!world.Check(new MoveAndEnterCommand(new PlayerId(2), 1001, world.Tick, hero.Id, jeep.Id)).Accepted, $"{rate}: a hero leaves it alone.");
            Check.That(world.Submit(new MoveAndEnterCommand(new PlayerId(2), 1001, world.Tick, grunt.Id, jeep.Id)).Accepted, $"{rate}: any infantry may enter it.");
            for (var step = 0; step < world.Frames(600) && !jeep.Captured; step++) world.Step();
            Check.That(jeep.Captured && !jeep.Unmanned && jeep.Owner == new PlayerId(2), $"{rate}: the infantry took it over.");
            world.Step();
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            Check.That(world.FindEntity(grunt.Id) is null, $"{rate}: the infantry is its driver now and gone.");

            var (bombs, bombFeeder, _) = RatedWorld(rate, files, NavTerrain());
            bombFeeder.Spawn(1, "Technical", x: 100, y: 100);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) bombs.Step();
            var technical = bombs.Entities.Single(value => value.Template.Name == "Technical");
            bombs.Damage(technical.Id, PilotShot(EntityId.Invalid));
            Check.That(technical.EffectivelyDead, $"{rate}: a car bomb's driver has a dead-man's trigger.");
        }
    }
}

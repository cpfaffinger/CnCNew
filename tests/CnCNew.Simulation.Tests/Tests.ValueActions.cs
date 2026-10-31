using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.World;

/// <summary>The three script actions no campaign map uses (F-22): <c>doNamedFireWeaponFollowingWaypointPath</c>, <c>doSkirmishAttackNearestGroupWithValue</c>, <c>doSkirmishCommandButtonOnMostValuable</c>.</summary>
static partial class Tests
{
    private const string ValueFixtureIni = """
        Weapon PathMissileGun
          PrimaryDamage = 500
          PrimaryDamageRadius = 30.0
          AttackRange = 200.0
          DamageType = EXPLOSION
          ClipSize = 1
          ClipReloadTime = 100000
          DelayBetweenShots = 100
          WeaponSpeed = 200
          ProjectileObject = PathMissile
          CapableOfFollowingWaypoints = Yes
        End
        Locomotor PathMissileLoco
          Surfaces = AIR
          Speed = 300
          MinSpeed = 0
          Acceleration = 3000
          Braking = 3000
          TurnRate = 3600
        End
        Object PathMissile
          KindOf = PROJECTILE SMALL_MISSILE
          Locomotor = SET_NORMAL PathMissileLoco
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = MissileAIUpdate Missile01
            FuelLifetime = 0
            IgnitionDelay = 0
            InitialVelocity = 0
            DistanceToTravelBeforeTurning = 0
            DistanceToTargetForLock = 0
            KillSelfDelay = 200
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object PathLauncher
          KindOf = SELECTABLE STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY PathMissileGun
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object CheapTarget
          KindOf = SELECTABLE INFANTRY
          VisionRange = 20.0
          ShroudClearingRange = 20.0
          BuildCost = 10
          Body = ActiveBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
        End
        Object DearTarget
          KindOf = SELECTABLE INFANTRY
          VisionRange = 20.0
          ShroudClearingRange = 20.0
          BuildCost = 5000
          Body = ActiveBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
        End
        """;

    private static IReadOnlyDictionary<string, string> ValueFiles() => new Dictionary<string, string>(RoamFiles()) { ["Data/INI/Object/ValueFixture.ini"] = ValueFixtureIni };

    public static void WeaponFollowsAWaypointPath()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Fire", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_FIRE_WEAPON_FOLLOWING_WAYPOINT_PATH", Scr.Unit("Launcher"), Scr.Named(MapScriptParameterType.WaypointPath, "Flight"))], oneShot: true),
        ], objects: [Placed("PathLauncher", 100m, 100m, "teamA", "Launcher"), Placed("Bystander", 110m, 300m, "teamB", "OnTheWay"), Placed("Bystander", 480m, 300m, "teamB", "AtTheEnd")],
            paths: [("Flight", [("A", 120m, 100m), ("B", 110m, 300m), ("C", 480m, 300m)])], extraFiles: ValueFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(45));
        var middle = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(middle.StateHash() == session.StateHash(), "A restored mission keeps the missile's route.");
        Run(session, session.Frames(240));
        Run(middle, middle.Frames(240));
        Check.That(middle.StateHash() == session.StateHash(), "And flies the same flight.");
        Check.That(!Hurt(session, "OnTheWay"), "A waypoint on the way is flown over at height.");
        Check.That(Hurt(session, "AtTheEnd"), "At the last waypoint the missile comes down and detonates.");
        Check.That(session.Engine.MissingActionHits.Count == 0, "The action is executed.");
    }

    public static void TeamAttacksTheNearestGroupWithValue()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SKIRMISH_ATTACK_NEAREST_GROUP_WITH_VALUE", Scr.Team("teamA"), Scr.Compare(Scr.Greater), Scr.Int(1000))], oneShot: true),
        ], objects: [Placed("MoodGunner", 100m, 200m, "teamA", "Hunter"), Placed("CheapTarget", 200m, 200m, "teamB", "Near"), Placed("DearTarget", 500m, 200m, "teamB", "Far")], extraFiles: ValueFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(30));
        var hunter = NamedIn(session, "Hunter")!;
        Check.That(session.World.IsAttackMoving(hunter), "The team attack-moves.");
        var goal = session.World.AttackMoveGoalOf(hunter)!.Value;
        Check.That(goal.X > Fixed.FromInt(400) && goal.X <= Fixed.FromInt(520), $"To the cell of the expensive target, past the cheap one ({goal.X}).");
        var below = MissionMap([Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SKIRMISH_ATTACK_NEAREST_GROUP_WITH_VALUE", Scr.Team("teamA"), Scr.Compare(Scr.Less), Scr.Int(1000))], oneShot: true)],
            objects: [Placed("MoodGunner", 100m, 200m, "teamA", "Hunter"), Placed("DearTarget", 500m, 200m, "teamB", "Far")], extraFiles: ValueFiles());
        var other = MissionSession.Start(MissionOptions.Standard, below.Fixture.Templates, below.Fixture.Fingerprint, below.Terrain, below.Map);
        Run(other, other.Frames(30));
        Check.That(!NamedIn(other, "Hunter")!.HasMoveTarget, "Another comparison sends nobody.");
    }

    public static void TeamUsesItsButtonOnTheMostValuable()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Go", [Scr.And(Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [Scr.Do("SKIRMISH_PERFORM_COMMANDBUTTON_ON_MOST_VALUABLE_OBJECT", Scr.Team("teamA"), Scr.Ability("Cmd_AlphaFire"), Scr.Real(150m), Scr.Bool(true))], oneShot: true),
        ], objects: [Placed("MoodGunner", 100m, 200m, "teamA", "Gunner"), Placed("CheapTarget", 140m, 200m, "teamB", "Cheap"), Placed("DearTarget", 170m, 200m, "teamB", "Dear"), Placed("DearTarget", 400m, 200m, "teamB", "Out")], extraFiles: ValueFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        session.World.SelfAcquisition = false;
        Run(session, session.Frames(30));
        session.Engine.SignalUiInteraction("go");
        Run(session, session.Frames(15));
        Check.That(Hurt(session, "Dear"), $"The button is used on the most expensive enemy in range ({string.Join("; ", session.Engine.Diagnostics.Keys)}, rejections {string.Join(",", session.World.RejectionCounts.Select((count, index) => (count, index)).Where(item => item.count > 0))}).");
        Check.That(!Hurt(session, "Cheap") && !Hurt(session, "Out"), "Not on a cheaper one, and not beyond the range.");
    }
}

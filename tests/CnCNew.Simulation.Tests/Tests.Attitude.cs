using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.World;

/// <summary>
/// The attitude of one object (F-22, <c>AIUpdateInterface::m_attitude</c>): scripts set it per object, a team's members keep it until they join another team, the map names it, a transport
/// makes its riders aggressive, and a computer player's sleeping unit ignores every order and fires at nothing.
/// </summary>
static partial class Tests
{
    private const string AttitudeFixtureIni = """
        Weapon MoodGun
          PrimaryDamage = 5
          AttackRange = 80.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Object MoodGunner
          KindOf = SELECTABLE INFANTRY
          VisionRange = 100.0
          TransportSlotCount = 1
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MoodGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
        End
        Object MoodCarrier
          KindOf = SELECTABLE VEHICLE TRANSPORT
          VisionRange = 100.0
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = TransportContain Transport01
            Slots = 4
            NumberOfExitPaths = 0
            GoAggressiveOnExit = Yes
          End
        End
        """;

    private static IReadOnlyDictionary<string, string> AttitudeFiles() => new Dictionary<string, string> { ["Data/INI/Object/AttitudeFixture.ini"] = AttitudeFixtureIni };

    private static MapObject PlacedWithMood(string template, decimal x, decimal y, string team, string name, int mood)
    {
        var placed = Placed(template, x, y, team, name);
        return placed with { Properties = new MapProperties([..placed.Properties.Items, new MapProperty("objectAggressiveness", MapPropertyType.Int, mood, 0m, string.Empty)]) };
    }

    private static bool Hurt(MissionSession session, string name) => NamedIn(session, name) is { Body: { } body } && body.Health < body.MaxHealth;

    public static void NamedAttitudeSleepIgnoresOrdersAndFiresAtNothing()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Mood", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_SET_ATTITUDE", Scr.Unit("Sleeper"), Scr.Int(-2))], oneShot: true),
            Scr.Script("Go", [Scr.And(Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [
                Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Sleeper"), Scr.Waypoint("Far")), Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Other"), Scr.Waypoint("Far"))], oneShot: true),
        ], objects: [Placed("MoodGunner", 100m, 100m, "teamB", "Sleeper"), Placed("MoodGunner", 100m, 300m, "teamB", "Other"), Placed("MoodGunner", 140m, 100m, "teamA", "Near"), Placed("MoodGunner", 140m, 300m, "teamA", "Far2")],
            extraFiles: AttitudeFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(90));
        Check.That(!Hurt(session, "Near"), "A sleeping computer unit fires at nothing in its range.");
        Check.That(Hurt(session, "Far2"), "A unit of normal attitude beside it does.");
        Check.That(Hurt(session, "Sleeper"), "A human unit has no mood: it fires at the sleeper.");
        var start = NamedIn(session, "Sleeper")!.X;
        session.Engine.SignalUiInteraction("go");
        Run(session, session.Frames(45));
        Check.That(NamedIn(session, "Sleeper")!.X == start, $"A sleeping computer unit ignores the script's move ({start} -> {NamedIn(session, "Sleeper")!.X}).");
        Check.That(session.World.IsAttackMoving(NamedIn(session, "Other")!) || NamedIn(session, "Other")!.HasMoveTarget || NamedIn(session, "Other")!.X > Fixed.FromInt(100), "The other unit obeys.");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash() && NamedIn(restored, "Sleeper")!.OwnAttitude == -2, "A restored mission keeps the object's attitude.");
    }

    public static void TeamAttitudeBelongsToTheMembersOfTheMoment()
    {
        var session = StartMission([
            Scr.Script("Mood", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("TEAM_SET_ATTITUDE", Scr.Team("teamB"), Scr.Int(-2)),
                Scr.Do("CREATE_NAMED_ON_TEAM_AT_WAYPOINT", Scr.Unit("Late"), Scr.Type("MoodGunner"), Scr.Team("teamB"), Scr.Waypoint("Mid"))], oneShot: true),
        ], objects: [Placed("MoodGunner", 100m, 100m, "teamB", "Early"), Placed("MoodGunner", 290m, 200m, "teamA", "Target")], extraFiles: AttitudeFiles());
        Run(session, session.Frames(60));
        var early = NamedIn(session, "Early")!;
        var late = NamedIn(session, "Late")!;
        Check.That(session.World.AttitudeOf(early) == Attitude.Sleep, "A member of the moment takes the attitude.");
        Check.That(session.World.AttitudeOf(late) == Attitude.Normal && Hurt(session, "Target"), "Who joins afterwards has the team's initial attitude, and fires.");
    }

    public static void MapObjectAggressivenessReplacesTheTeamsAttitude()
    {
        var session = StartMission([Scr.Script("Idle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NO_OP")], oneShot: true)],
            objects: [PlacedWithMood("MoodGunner", 100m, 100m, "teamB", "Dozy", -2), PlacedWithMood("MoodGunner", 100m, 300m, "teamB", "Plain", 0), Placed("MoodGunner", 140m, 100m, "teamA", "Near"), Placed("MoodGunner", 140m, 300m, "teamA", "Other2")],
            extraFiles: AttitudeFiles());
        Run(session, session.Frames(60));
        Check.That(!Hurt(session, "Near"), "objectAggressiveness -2 makes the placed object sleep.");
        Check.That(Hurt(session, "Other2") && NamedIn(session, "Plain")!.OwnAttitude is null, "An object with the attitude of its team keeps no state of its own.");
    }

    public static void TransportMakesItsRidersAggressive()
    {
        var session = StartMission([
            Scr.Script("Board", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_ENTER_NAMED", Scr.Unit("Rider"), Scr.Unit("Carrier"))], oneShot: true),
            Scr.Script("Out", [Scr.And(Scr.If("FLAG", Scr.Flag("out"), Scr.Bool(true)))], [Scr.Do("NAMED_EXIT_ALL", Scr.Unit("Carrier"))], oneShot: true),
        ], objects: [Placed("MoodGunner", 100m, 100m, "teamB", "Rider"), Placed("MoodCarrier", 120m, 100m, "teamB", "Carrier")], extraFiles: AttitudeFiles());
        Run(session, session.Frames(300));
        var rider = NamedIn(session, "Rider")!;
        Check.That(rider.ContainedBy.IsValid, "The rider boarded.");
        Check.That(session.World.AttitudeOf(rider) == Attitude.Normal, "Inside it has its old attitude.");
        session.Engine.SignalUiInteraction("out");
        Run(session, session.Frames(60));
        Check.That(!rider.ContainedBy.IsValid && session.World.AttitudeOf(rider) == Attitude.Aggressive, $"GoAggressiveOnExit: the rider left with the aggressive attitude ({session.World.AttitudeOf(rider)}).");
    }
}

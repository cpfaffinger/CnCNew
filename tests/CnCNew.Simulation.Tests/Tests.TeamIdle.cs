using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Scripting;

/// <summary>
/// F-21: "idle" of a script is the state of the AI (<c>AIUpdateInterface::isIdle</c>, <c>AIGroup::isIdle</c>, <c>Team::updateState</c>), not "has no move". The original has no state machine
/// of a team: <c>TEAM_STATE_IS</c> reads the text <c>TEAM_SET_STATE</c> wrote (<c>Team::getState</c>), which the VM has done since Phase 13.
/// </summary>
static partial class Tests
{
    private const string TeamIdleFixtureIni = """
        Object FrailTarget
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 30.0
            InitialHealth = 30.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Sentinel
          KindOf = SELECTABLE STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object IdleHacker
          KindOf = INFANTRY SELECTABLE MONEY_HACKER
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = HackInternetAIUpdate Hack01
            UnpackTime = 300
            PackTime = 300
            PackUnpackVariationFactor = 0.0
            CashUpdateDelay = 1000
            CashUpdateDelayFast = 500
            RegularCashAmount = 5
            VeteranCashAmount = 6
            EliteCashAmount = 8
            HeroicCashAmount = 10
          End
        End
        """;

    private static IReadOnlyDictionary<string, string> TeamIdleFiles() => new Dictionary<string, string>(RoamFiles()) { ["Data/INI/Object/TeamIdleFixture.ini"] = TeamIdleFixtureIni };

    public static void SequentialScriptWaitsForAFight()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Steps", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_FLAG", Scr.Flag("first"), Scr.Bool(true))], subroutine: true),
            Scr.Script("Start", [Scr.And(Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [Scr.Do("UNIT_EXECUTE_SEQUENTIAL_SCRIPT", Scr.Unit("Gunner"), Scr.Script("Steps"))], oneShot: true),
        ], objects: [Placed("MoodGunner", 100m, 200m, "teamA", "Gunner"), Placed("FrailTarget", 150m, 200m, "teamB", "Target")], extraFiles: TeamIdleFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(30));
        Check.That(Hurt(session, "Target"), "The unit fights by itself, with no order.");
        session.Engine.SignalUiInteraction("go");
        Run(session, session.Frames(6));
        Check.That(!session.Engine.FlagValue("first"), "An attack is no idle state: the sequential script waits.");
        Run(session, session.Frames(150));
        Check.That(NamedIn(session, "Target") is null or { EffectivelyDead: true }, "The target fell.");
        Check.That(session.Engine.FlagValue("first"), "With the attack over the script goes on.");
    }

    public static void TeamIdleReadsTheStateOfTheAi()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("PostIdle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_FLAG", Scr.Flag("post"), Scr.Bool(true))], subroutine: true),
            Scr.Script("FightIdle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("fight"))], subroutine: true),
            Scr.Script("HackIdle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_FLAG", Scr.Flag("hack"), Scr.Bool(true))], subroutine: true),
        ], objects: [Placed("Sentinel", 100m, 100m, "Post", "Guard"), Placed("MoodGunner", 100m, 300m, "Fighters", "Gunner"), Placed("Bystander", 150m, 300m, "teamB", "Target"), Placed("IdleHacker", 400m, 100m, "Hackers", "Hacker")],
            extraFiles: TeamIdleFiles());
        MapProperties Team(string name, string onIdle) => new([Scr.Text("teamName", name), Scr.Text("teamOwner", PlayerA), Scr.Flag("teamIsSingleton", true), Scr.Text("teamOnIdleScript", onIdle)]);
        map = map with { Teams = [..map.Teams, Team("Post", "PostIdle"), Team("Fighters", "FightIdle"), Team("Hackers", "HackIdle")] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        NamedIn(session, "Hacker")!.Modules.OfType<HackInternetAIUpdate>().Single().StartHacking(session.World);
        Run(session, session.Frames(45));
        Check.That(session.Engine.FlagValue("post"), "A team whose only member cannot move but has an AI goes idle (Team::updateState asks for the AI, not for a locomotor).");
        Check.That(Hurt(session, "Target"), "The fight runs.");
        var before = session.Engine.CounterValue("fight");
        Run(session, session.Frames(45));
        Check.That(session.Engine.CounterValue("fight") == before, "A team in a fight is not idle.");
        Check.That(!session.Engine.FlagValue("hack"), "A hacker at work is not idle.");
    }
}

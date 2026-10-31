using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;

/// <summary><c>SCRIPTING_OVERRIDE_HULK_LIFETIME</c> (F-22): <c>GameLogic::setHulkMaxLifetimeOverride</c>, read by <c>LifetimeUpdate</c> and <c>SlowDeathBehavior::beginSlowDeath</c> for <c>KINDOF_HULK</c>.</summary>
static partial class Tests
{
    private const string HulkFixtureIni = """
        Object ScriptHulk
          KindOf = HULK
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = LifetimeUpdate Lifetime01
            MinLifetime = 100000
            MaxLifetime = 100000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object ScriptSinker
          KindOf = HULK
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 100000
          End
        End
        Object ScriptWreck
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = LifetimeUpdate Lifetime01
            MinLifetime = 100000
            MaxLifetime = 100000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """;

    public static void HulkLifetimeOverride()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Hurry", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("SCRIPTING_OVERRIDE_HULK_LIFETIME", Scr.Real(2m)),
                Scr.Do("CREATE_NAMED_ON_TEAM_AT_WAYPOINT", Scr.Unit("New"), Scr.Type("ScriptHulk"), Scr.Team("teamB"), Scr.Waypoint("Mid")),
                Scr.Do("CREATE_NAMED_ON_TEAM_AT_WAYPOINT", Scr.Unit("Wreck"), Scr.Type("ScriptWreck"), Scr.Team("teamB"), Scr.Waypoint("Far")),
                Scr.Do("NAMED_KILL", Scr.Unit("Sinker"))], oneShot: true),
            Scr.Script("Off", [Scr.And(Scr.If("FLAG", Scr.Flag("off"), Scr.Bool(true)))], [
                Scr.Do("SCRIPTING_OVERRIDE_HULK_LIFETIME", Scr.Real(-1m)),
                Scr.Do("CREATE_NAMED_ON_TEAM_AT_WAYPOINT", Scr.Unit("After"), Scr.Type("ScriptHulk"), Scr.Team("teamB"), Scr.Waypoint("Spawn"))], oneShot: true),
        ], objects: [Placed("ScriptHulk", 100m, 100m, "teamB", "Old"), Placed("ScriptSinker", 150m, 100m, "teamB", "Sinker")],
            extraFiles: new Dictionary<string, string> { ["Data/INI/Object/HulkFixture.ini"] = HulkFixtureIni });
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(20));
        Check.That(NamedIn(session, "Sinker") is { } sinking && sinking.EffectivelyDead, "A second after the end of a slow death is still to come.");
        var sinkerId = NamedIn(session, "Sinker")!.Id;
        Run(session, session.Frames(16));
        Check.That(session.World.FindEntity(sinkerId) is null or { DestroyRequested: true }, "A hulk's slow death takes one second under the override, not its data's delay.");
        Run(session, session.Frames(15));
        Check.That(NamedIn(session, "New") is { EffectivelyDead: false }, "After 1.7 s the new hulk still lives.");
        Run(session, session.Frames(15));
        Check.That(NamedIn(session, "New") is null or { EffectivelyDead: true } or { DestroyRequested: true }, "After 2 s a hulk made under the override is gone.");
        Check.That(NamedIn(session, "Old") is { EffectivelyDead: false }, "A hulk made before the override keeps the lifetime it drew.");
        Check.That(NamedIn(session, "Wreck") is { EffectivelyDead: false }, "An object that is no hulk is not concerned.");
        session.Engine.SignalUiInteraction("off");
        Run(session, session.Frames(90));
        Check.That(session.World.HulkLifetimeOverrideTicks == -1 && NamedIn(session, "After") is { EffectivelyDead: false }, "A negative value turns the override off.");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash(), "A restored mission matches.");
    }
}

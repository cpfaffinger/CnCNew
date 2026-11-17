using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.World;

/// <summary>Issue #423: four small differences to the original - recruiting a held unit, a stunned unit's locomotor, the repulsor filter's "able to attack", a self-upgrade that starts active.</summary>
static partial class Tests
{
    /// <summary>
    /// <c>Team::tryToRecruit</c> skips an object that <c>isDisabledByType(DISABLED_HELD)</c> ("Don't recruit held units"): a soldier a script holds (<c>NAMED_SET_HELD</c>) is never recruited,
    /// the free ones of the same team are.
    /// </summary>
    public static void AHeldUnitIsNotRecruited()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Recruit", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_SET_HELD", Scr.Unit("Held"), Scr.Bool(true)), Scr.Do("RECRUIT_TEAM", Scr.Team("Strike"), Scr.Real(0m))], oneShot: true),
        ], objects: [Placed("BetaSoldier", 100m, 200m, "teamPlayerB", "Free1"), Placed("BetaSoldier", 120m, 200m, "teamPlayerB", "Held"), Placed("BetaSoldier", 140m, 200m, "teamPlayerB", "Free2")]);
        var strikeTeam = new MapProperties([Scr.Text("teamName", "Strike"), Scr.Text("teamOwner", PlayerB), Scr.Flag("teamIsSingleton", true), Scr.Text("teamHome", "Far"),
            new MapProperty("teamProductionPriority", MapPropertyType.Int, 5, 0m, string.Empty),
            Scr.Text("teamUnitType1", "BetaSoldier"), new MapProperty("teamUnitMinCount1", MapPropertyType.Int, 1, 0m, string.Empty), new MapProperty("teamUnitMaxCount1", MapPropertyType.Int, 3, 0m, string.Empty)]);
        map = map with { Teams = [..map.Teams, strikeTeam, new([Scr.Text("teamName", "teamPlayerB"), Scr.Text("teamOwner", PlayerB), Scr.Flag("teamIsSingleton", true)])] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(10));
        var (free1, held, free2) = (NamedIn(session, "Free1")!, NamedIn(session, "Held")!, NamedIn(session, "Free2")!);
        Check.That((session.World.DisabledFlagsOf(held) & DisabledMask.Held) != DisabledMask.None, "Setup: the script holds the soldier.");
        var strike = session.Engine.TeamInstances.First(team => team.Prototype == "Strike");
        Check.That(strike.Members.Count == 2 && !strike.Members.Contains(held.Id.Value) && strike.Members.Contains(free1.Id.Value) && strike.Members.Contains(free2.Id.Value),
            $"A held soldier is not recruited, the free ones are ({strike.Members.Count} members, held one in: {strike.Members.Contains(held.Id.Value)}).");
    }
}

using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.Testing;

/// <summary>What a headless mission run observed; identical inputs give an identical report, field for field.</summary>
public sealed record MissionRunReport(
    string Map, int Players, int PlacedObjects, int SkippedObjects, int SkippedTemplates, long Ticks, MissionOutcome Outcome, long OutcomeTick, bool Ended,
    long ExecutedScripts, long ExecutedActions, long ForwardedActions, long Entities, long Shots, ulong FinalHash, ulong Chain,
    ImmutableArray<(string Name, long Count)> MissingActions, ImmutableArray<(string Name, long Count)> MissingConditions, ImmutableArray<(string Name, long Count)> Diagnostics,
    ImmutableArray<(string Template, int Count)> Skipped)
{
    public long MissingActionTotal => MissingActions.Sum(item => item.Count);
    public long MissingConditionTotal => MissingConditions.Sum(item => item.Count);
}

/// <summary>Runs a mission headless for a number of ticks (or until its script ends it) and reports; the building block of the mission audit and the mission tests.</summary>
public static class MissionScenario
{
    public static MissionRunReport Run(MissionOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, MapData map, long ticks, Action<MissionSession>? perTick = null)
    {
        var session = MissionSession.Start(options, catalog, fingerprint, terrain, map);
        return Continue(session, terrain.Name, ticks, perTick);
    }

    /// <summary>Steps an existing session (fresh or restored) until <paramref name="ticks"/> in total, or its end, and reports.</summary>
    public static MissionRunReport Continue(MissionSession session, string name, long ticks, Action<MissionSession>? perTick = null, ulong chainSeed = 0)
    {
        var chain = chainSeed == 0 ? StateHash.Seed : chainSeed;
        while (session.World.Tick < ticks && !session.IsOver)
        {
            var result = session.Step();
            chain = StateHash.Mix(chain, result.StateHash);
            perTick?.Invoke(session);
        }
        return Report(session, name, chain);
    }

    public static MissionRunReport Report(MissionSession session, string name, ulong chain)
    {
        var engine = session.Engine;
        return new MissionRunReport(name, session.Program.Players.Count(player => !player.Id.IsSystem && player.Name.Length > 0), session.Program.Objects.Length,
            session.SkippedObjects.Sum(item => item.Count), session.SkippedObjects.Length, session.World.Tick, engine.Outcome, engine.OutcomeTick, engine.IsEnded,
            engine.ExecutedScripts, engine.ExecutedActions, engine.ForwardedActions, session.World.Entities.Count, session.CombatStats.Shots,
            session.StateHash(), chain,
            [..engine.MissingActionHits.Select(pair => (pair.Key, pair.Value)).OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal)],
            [..engine.MissingConditionHits.Select(pair => (pair.Key, pair.Value)).OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal)],
            [..engine.Diagnostics.Select(pair => (pair.Key, pair.Value)).OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal)],
            session.SkippedObjects);
    }

    /// <summary>
    /// Saves a session half way, restores it, steps both to the end and compares every tick's combined world/script hash. Returns the two reports; the
    /// caller asserts equality (<see cref="MissionRunReport.Chain"/> and <see cref="MissionRunReport.FinalHash"/> cover the whole continuation).
    /// </summary>
    public static (MissionRunReport Original, MissionRunReport Restored, int SnapshotBytes) VerifyContinuation(MissionOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, MapData map, long ticks)
    {
        var original = MissionSession.Start(options, catalog, fingerprint, terrain, map);
        var half = ticks / 2;
        ulong chain = StateHash.Seed;
        while (original.World.Tick < half && !original.IsOver) chain = StateHash.Mix(chain, original.Step().StateHash);
        var bytes = MissionSnapshot.Write(original);
        var restored = MissionSnapshot.Restore(bytes, catalog, fingerprint, terrain, map);
        var restoredChain = chain;
        while (original.World.Tick < ticks && !original.IsOver) chain = StateHash.Mix(chain, original.Step().StateHash);
        while (restored.World.Tick < ticks && !restored.IsOver) restoredChain = StateHash.Mix(restoredChain, restored.Step().StateHash);
        return (Report(original, terrain.Name, chain), Report(restored, terrain.Name, restoredChain), bytes.Length);
    }
}

/// <summary>What happened when a mission was pushed toward one of its endings.</summary>
/// <param name="Reached">The expected outcome was reached.</param>
/// <param name="Outcome">The outcome that was reached (<see cref="MissionOutcome.None"/> when the run timed out).</param>
/// <param name="EndingScripts">Scripts of the map that end the mission this way (contain the matching VICTORY/DEFEAT action), with whether each was active at the end.</param>
/// <param name="Unmet">For an ending not reached: per ending script, the conditions that were false at the end (labels); empty when reached.</param>
public sealed record MissionProbeResult(bool Reached, MissionOutcome Outcome, long TicksToOutcome, ImmutableArray<string> EndingScripts, ImmutableArray<string> Unmet, MissionRunReport Report);

public static class MissionProbe
{
    /// <summary>
    /// Headless proof that an ending exists and is reachable: lets the mission settle, then destroys every object of the players hostile to the viewer
    /// (<paramref name="victory"/>) or every object of the viewer and its allies (defeat) and waits for the script outcome. It does not claim every objective
    /// path works; it checks the scripted endings that follow from elimination, and names the ending scripts whose conditions stayed false otherwise.
    /// </summary>
    public static MissionProbeResult Run(MissionOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, MapData map, bool victory, long settleTicks = 150, long waitTicks = 600)
    {
        var session = MissionSession.Start(options, catalog, fingerprint, terrain, map);
        while (session.World.Tick < settleTicks && !session.IsOver) session.Step();
        var local = MissionSession.LocalPlayerOf(session.Program);
        var world = session.World;
        var toKill = world.Entities.Where(entity => !entity.Owner.IsSystem && (victory ? world.AreEnemies(local, entity.Owner) : entity.Owner == local || world.AreAllies(local, entity.Owner)))
            .OrderBy(entity => entity.Id.Value).Select(entity => entity.Id).ToArray();
        foreach (var id in toKill) world.Kill(id);
        var killTick = world.Tick;
        var wanted = victory ? MissionOutcome.Victory : MissionOutcome.Defeat;
        var actionName = victory ? "VICTORY" : "DEFEAT";
        while (world.Tick < killTick + waitTicks && session.Engine.Outcome == MissionOutcome.None && !session.IsOver) session.Step();
        var ending = session.Program.Units.Where(unit => unit.Script.Actions.Concat(unit.Script.FalseActions).Any(action => action.TypeName == actionName || (victory && action.TypeName == "QUICKVICTORY"))).ToArray();
        var unmet = ImmutableArray.CreateBuilder<string>();
        if (session.Engine.Outcome != wanted)
            foreach (var unit in ending.Take(6))
                unmet.Add($"{unit.Name}{(session.Engine.IsScriptActive(unit.Name) ? "" : " [inactive]")}: " +
                          string.Join(", ", session.Engine.ExplainScript(unit.Name).Where(item => !item.Result).Select(item => item.Label)));
        var outcomeTick = session.Engine.OutcomeTick;
        return new MissionProbeResult(session.Engine.Outcome == wanted, session.Engine.Outcome, outcomeTick < 0 ? -1 : outcomeTick - killTick,
            [..ending.Select(unit => unit.Name + (session.Engine.IsScriptActive(unit.Name) ? "" : " [inactive]"))], unmet.ToImmutable(), MissionScenario.Report(session, terrain.Name, StateHash.Seed));
    }
}

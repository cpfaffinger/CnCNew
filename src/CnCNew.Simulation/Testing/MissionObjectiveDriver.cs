using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.Testing;

/// <summary>What the objective driver did and whether the mission's own scripts ended it.</summary>
/// <param name="ReachedByScript">The mission ended with the wanted outcome by a script's own <c>VICTORY</c>/<c>DEFEAT</c> action (no generic elimination was applied).</param>
/// <param name="OutcomeScript">The script whose action decided the mission (empty when none did).</param>
/// <param name="Interventions">How many world changes the driver applied; <paramref name="Log"/> names each.</param>
/// <param name="Commands">How many commands (orders, productions) the driver issued through the session.</param>
/// <param name="WorldWrites">How many leaf applications wrote to the world directly; zero in a commands-only run.</param>
/// <param name="Declined">Commands-only runs: the direct writes that were declined, so the run had to do without them.</param>
/// <param name="Unmet">When the ending was not reached: per ending script what stayed open (blockers the driver cannot satisfy, leaves it kept applying, the chain of false timers/counters/flags).</param>
public sealed record ObjectiveDriveResult(bool ReachedByScript, MissionOutcome Outcome, string OutcomeScript, long OutcomeTick, int Interventions, ImmutableArray<string> Log, ImmutableArray<string> Unmet,
    int Commands = 0, int WorldWrites = 0, ImmutableArray<string> Declined = default);

/// <summary>
/// A headless "objective driver" (<c>docs/SCRIPT-ENGINE.md</c> §9): instead of eliminating a side as <see cref="MissionProbe"/> does, it works backwards from the mission's own
/// <c>VICTORY</c>/<c>DEFEAT</c> scripts. Every few ticks it plans, from the engine's current state, what is still missing for each ending script — the scripts that enable or
/// call it, the scripts that start the timers and set the counters and flags it reads, and, at the bottom, the world-state conditions (a named object destroyed, a team inside an
/// area, credits, ownership, a count of objects) — and satisfies the world-state leaves the way a player pursuing the objective would (a move order first, a placed unit when
/// the order does not get there). Timers are waited out, scripts run on their own, and the run is counted only when a script's <em>own</em> ending action fires.
/// <para>
/// Test and tool code, not simulation behaviour: it writes to the world directly (like <see cref="MissionProbe"/>) and issues orders through the session's command counter.
/// The plan is a pure function of the engine's state (<see cref="ScriptEngine.ExplainScript"/> leaves the engine untouched), so a run is deterministic. A leaf the driver has no
/// intervention for is a <em>blocker</em>: it is named in <see cref="ObjectiveDriveResult.Unmet"/> and never silently skipped.
/// </para>
/// </summary>
public static class MissionObjectiveDriver
{
    /// <summary>
    /// Units: <paramref name="maxTicks"/> is the run budget in ticks of the catalog's rate; <paramref name="spacing"/> (distance of two planning rounds) and
    /// <paramref name="settleTicks"/> (game time before the first round) are logic frames of the original (1/30 s) and scaled here, like every cooldown of the driver's leaves -
    /// the driver plans at the same moments of game time at every rate. At 30 Hz a frame is a tick.
    /// </summary>
    public static ObjectiveDriveResult Run(MissionOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, MapData map, bool victory, long maxTicks = 4500, int spacing = 15, long settleTicks = 60, Action<MissionSession>? observe = null, bool commandsOnly = false, int agentThreshold = 0)
    {
        var session = MissionSession.Start(options, catalog, fingerprint, terrain, map);
        var driver = new ObjectiveDriver(session, victory, commandsOnly, agentThreshold);
        var world = session.World;
        var engine = session.Engine;
        var settle = world.Time.OriginalFrames(settleTicks);
        var roundSpacing = world.Time.OriginalFrames(spacing);

        while (world.Tick < settle && !session.IsOver) { driver.Act(); session.Step(); observe?.Invoke(session); }
        long nextAt = world.Tick;
        while (world.Tick < maxTicks && !session.IsOver && engine.Outcome == MissionOutcome.None)
        {
            if (world.Tick >= nextAt)
            {
                driver.Round();
                nextAt = world.Tick + roundSpacing;
            }
            driver.Act();
            session.Step();
            observe?.Invoke(session);
        }
        var wanted = victory ? MissionOutcome.Victory : MissionOutcome.Defeat;
        return new ObjectiveDriveResult(engine.Outcome == wanted, engine.Outcome, engine.OutcomeScript, engine.OutcomeTick, driver.Interventions, driver.Log, engine.Outcome == wanted ? [] : [..driver.Unmet(), ..driver.Declined.Select(line => "declined world write: " + line), ..new[] { driver.AgentSummary() }.Where(line => line.Length > 0), ..driver.Survivors(), ..driver.PowerCarriers()],
            driver.Commands + (driver.AgentStats?.Commands ?? 0), driver.WorldWrites, driver.Declined);
    }
}

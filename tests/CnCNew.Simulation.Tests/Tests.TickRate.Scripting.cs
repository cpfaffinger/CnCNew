using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

/// <summary>
/// Phase 20, stage 2 (scripts, sessions, skirmish AI, scenarios): what was a bare tick number is a number of logic frames of the original now. Each test
/// measures a duration or a rhythm at 30 Hz and checks that at 60 and 120 Hz it is exactly twice and four times as many ticks, on the same frame of the
/// original; literals that mean order ("the next step") must be the same number of ticks at every rate.
/// </summary>
static partial class Tests
{
    private static readonly (SimulationRate Rate, int Factor)[] ScriptingRates = [(SimulationRate.Hz30, 1), (SimulationRate.Hz60, 2), (SimulationRate.Hz120, 4)];

    private static MissionSession RatedMission(SimulationRate rate, MapScript[] scripts, MissionOptions? options = null)
    {
        var (map, fixture, terrain) = MissionMap(scripts, rate: rate);
        return MissionSession.Start(options ?? MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
    }

    /// <summary>Steps until the engine has ended and returns how many steps that took after the one that decided the outcome.</summary>
    private static long StepsFromOutcomeToEnd(MissionSession session, long limit)
    {
        long decidedAt = -1;
        for (long step = 0; step < limit; step++)
        {
            session.Step();
            if (decidedAt < 0 && session.Engine.Outcome != MissionOutcome.None) decidedAt = step;
            if (session.Engine.IsEnded) return step - decidedAt;
        }
        return -1;
    }

    public static void TickRateScriptEndGameDelay()
    {
        foreach (var (rate, factor) in ScriptingRates)
        {
            var victory = RatedMission(rate, [Scr.Script("Win", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("VICTORY")], oneShot: true)]);
            Check.That(victory.Engine.EndGameDelay == ScriptEngine.EndGameDelayFrames * factor, $"{rate}: the end-game delay is 120 frames of the original in ticks.");
            var steps = StepsFromOutcomeToEnd(victory, 1_000);
            Check.That(steps == ScriptEngine.EndGameDelayFrames * factor, $"{rate}: the mission ends {ScriptEngine.EndGameDelayFrames} frames of the original after VICTORY (got {steps} ticks).");

            // QUICKVICTORY is order ("the next update"), not time: one step at every rate.
            var quick = RatedMission(rate, [Scr.Script("Win", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("QUICKVICTORY")], oneShot: true)]);
            var quickSteps = StepsFromOutcomeToEnd(quick, 100);
            Check.That(quickSteps == TickOrder.Next, $"{rate}: QUICKVICTORY ends the mission in the next update at every rate (got {quickSteps}).");
        }
    }

    public static void TickRateScriptPresentationLatency()
    {
        const int latencyFrames = 45;
        foreach (var (rate, factor) in ScriptingRates)
        {
            var session = RatedMission(rate,
                [Scr.Script("Talk", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SPEECH_PLAY", Scr.Named(MapScriptParameterType.Dialog, "Hello"), Scr.Bool(false))], oneShot: true)],
                MissionOptions.Standard with { PresentationLatencyTicks = latencyFrames });
            Check.That(session.Options.PresentationLatencyTicks == latencyFrames, $"{rate}: the option keeps the value the host gave (frames of the original).");
            long pending = 0;
            for (var step = 0; step < 400; step++)
            {
                session.Step();
                if (session.Engine.PendingPresentation.Contains("Hello")) pending++;
            }
            // Started in the update of tick 0, expired at the top of the update of tick latency: pending after the steps 0 .. latency - 1.
            Check.That(pending == latencyFrames * factor, $"{rate}: a speech counts as playing for {latencyFrames} frames of the original (got {pending} ticks).");
            var output = session.Engine.DrainOutputs();
            Check.That(output.Length == 1 && output[0].Tick == 0 && output[0].Arguments[0] == "Hello", $"{rate}: the forwarded arguments leave the simulation untouched.");
        }
    }

    public static void TickRateMissionProbeWaitsInGameTime()
    {
        foreach (var (rate, factor) in ScriptingRates)
        {
            var (map, fixture, terrain) = MissionMap([], rate: rate);
            var probe = MissionProbe.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, victory: true, settleTicks: 10, waitTicks: 20);
            Check.That(!probe.Reached && probe.Report.Ticks == 30 * factor, $"{rate}: the probe settles and waits in frames of the original (ran {probe.Report.Ticks} ticks).");
        }
    }

    private static SkirmishMatchSession RatedSkirmish(SimulationRate rate, SkirmishMatchOptions options, bool human)
    {
        var fixture = new SyntheticFixture(rate, files: SkirmishFiles());
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish");
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        return SkirmishMatchSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map.Objects, [..participants.Select(participant => new SkirmishSlot(participant, IsHuman: human))]);
    }

    public static void TickRateSkirmishSessionRules()
    {
        const int graceFrames = 60;
        const int scienceFrames = 40;
        const int killFrame = 100;
        long victory30 = -1;
        foreach (var (rate, factor) in ScriptingRates)
        {
            // Budget in ticks (scaled by hand here), rule values in frames of the original (the same numbers at every rate).
            var session = RatedSkirmish(rate, new SkirmishMatchOptions(0xC0FFEE06, 7, 2_000 * factor, graceFrames, 500, 500, scienceFrames), human: true);
            while (!session.IsOver)
            {
                if (session.World.Tick == killFrame * factor)
                    foreach (var id in session.World.Entities.Where(entity => entity.Owner == new PlayerId(2)).OrderBy(entity => entity.Id.Value).Select(entity => entity.Id).ToArray())
                        session.World.Kill(id);
                session.Step();
            }
            Check.That(session.Winner == new PlayerId(1) && session.VictoryTick > killFrame * factor, $"{rate}: the surviving player wins (victory tick {session.VictoryTick}).");
            Check.That(session.VictoryTick % (15 * factor) == 0, $"{rate}: the victory check runs every 15 frames of the original (victory tick {session.VictoryTick}).");
            Check.That(session.EndTick == session.VictoryTick + TickOrder.Next + graceFrames * factor, $"{rate}: the grace period is {graceFrames} frames of the original (end {session.EndTick}, victory {session.VictoryTick}).");
            if (factor == 1) victory30 = session.VictoryTick;
            else Check.That(session.VictoryTick == victory30 * factor, $"{rate}: the victory is noticed on the same frame of the original as at 30 Hz ({session.VictoryTick} against {victory30}).");

            var science = session.ToReplay().Commands.OfType<GiveSciencePointsCommand>().Select(command => command.Tick).Distinct().ToArray();
            var expected = Enumerable.Range(1, (int)(session.VictoryTick / (scienceFrames * factor))).Select(index => (long)index * scienceFrames * factor).ToArray();
            Check.That(science.Length > 0 && science.SequenceEqual(expected), $"{rate}: a science point every {scienceFrames} frames of the original until the victory (got {string.Join(",", science)}).");
        }
    }

    public static void TickRateSkirmishAiRhythm()
    {
        const int frames = 600;
        long firstCommand30 = -1;
        foreach (var (rate, factor) in ScriptingRates)
        {
            var session = RatedSkirmish(rate, new SkirmishMatchOptions(0xC0FFEE06, 7, frames * factor, 60, 500, 500, 0), human: false);
            while (!session.IsOver) session.Step();
            var commands = session.ToReplay().Commands.Where(command => !command.Player.IsSystem).ToArray();
            Check.That(commands.Length > 4 && commands.Select(command => command.Player).Distinct().Count() == 2, $"{rate}: both AIs issue commands ({commands.Length}).");
            // At 30 Hz an AI thinks when (tick + player % 5) % 15 == 0; period and stagger are frames of the original, so the same frames think at every rate.
            foreach (var command in commands)
                Check.That(command.Tick % factor == 0 && (command.Tick / factor + command.Player.Value % 5) % 15 == 0,
                    $"{rate}: {command.GetType().Name} of player {command.Player.Value} at tick {command.Tick} lies on the player's think frame.");
            var first = commands.Min(command => command.Tick);
            if (factor == 1) firstCommand30 = first;
            else Check.That(first == firstCommand30 * factor, $"{rate}: the first AI command falls on the same frame of the original as at 30 Hz ({first} against {firstCommand30}).");
            foreach (var ai in session.Ais)
                Check.That(ai!.Stats.AttackTicks % (15 * factor) == 0, $"{rate}: the attack time is counted in think intervals of ticks.");
        }
    }

    public static void TickRateReferenceMatchGeneratorRhythm()
    {
        const int frames = 1_000;
        foreach (var (rate, factor) in ScriptingRates)
        {
            var fixture = new SyntheticFixture(rate, files: ReferenceMatchFiles());
            var options = ReferenceMatchOptions.Standard with { MaxTicks = frames * factor, CheckpointInterval = 500, ArenaRadius = 400, GraceTicks = 100, IncomeInterval = 300 };
            var scenario = new ReferenceMatchScenario(options);
            var replay = scenario.Record(fixture.Templates, fixture.Fingerprint);
            var income = replay.Commands.OfType<GiveMoneyCommand>().Select(command => command.Tick).Distinct().ToArray();
            var incomeEnd = scenario.VictoryTick >= 0 ? scenario.VictoryTick : frames * (long)factor - 1;
            var expected = Enumerable.Range(1, (int)(incomeEnd / (300 * factor))).Select(index => (long)index * 300 * factor).ToArray();
            Check.That(income.Length > 0 && income.SequenceEqual(expected), $"{rate}: the income is paid every 300 frames of the original (got {string.Join(",", income)}).");
            // The fixture of this test has no supply economy; where a carrier exists its dock order is order, not time.
            Check.That(replay.Commands.OfType<DockCommand>().All(command => command.Tick == TickOrder.Next), $"{rate}: the dock orders go out in the step after the spawns, at every rate.");
            var production = replay.Commands.OfType<QueueProductionCommand>().ToArray();
            Check.That(production.Length > 0 && production.All(command => command.Tick % (30 * factor) == 0), $"{rate}: production orders every 30 frames of the original.");
            var orders = replay.Commands.Where(command => command is PursueCommand or FireWeaponCommand).ToArray();
            Check.That(orders.Length > 0 && orders.All(command => command.Tick % (15 * factor) == 0), $"{rate}: move and fire orders every 15 frames of the original.");
            if (scenario.VictoryTick >= 0)
                Check.That(scenario.VictoryTick % (15 * factor) == 0 && replay.Header.EndTick == scenario.VictoryTick + TickOrder.Next + 100 * factor, $"{rate}: victory check and grace period are frames of the original.");
        }
    }

    public static void TickRateStressScheduleCountsSteps()
    {
        // The stress schedule is a load per simulation step and deliberately not scaled (StressScenario): the same commands fall on the same steps at every rate.
        long[]? ghosts30 = null;
        foreach (var (rate, _) in ScriptingRates)
        {
            var replay = new StressScenario(ShortOptions).Record(new SyntheticFixture(rate));
            var ghosts = replay.Commands.OfType<SpawnCommand>().Where(command => command.Template == "Ghost").Select(command => command.Tick).ToArray();
            Check.That(ghosts.Length > 2 && ghosts.All(tick => tick % 40 == 0), $"{rate}: a ghost every 40 steps.");
            var timers = replay.Commands.OfType<SetTimerCommand>().Where(command => !command.Target.IsValid).Select(command => command.Tick).ToArray();
            Check.That(timers.Length > 0 && timers.All(tick => tick % 500 == 250), $"{rate}: a world timer every 500 steps, phase 250.");
            ghosts30 ??= ghosts;
            Check.That(ghosts.SequenceEqual(ghosts30), $"{rate}: the schedule is the same in steps as at 30 Hz.");
        }
    }
}

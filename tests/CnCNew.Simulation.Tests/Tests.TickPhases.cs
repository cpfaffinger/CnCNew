using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>The measuring hook of <c>SimTool perf</c> (<see cref="ITickPhaseObserver"/>): it sees every phase once per tick in the documented order and changes nothing.</summary>
static partial class Tests
{
    /// <summary>The world's own phases in the order of <c>SimulationWorld.Step</c>; the enum declares them in the same order.</summary>
    private static readonly TickPhase[] WorldPhases =
    [
        TickPhase.Commands, TickPhase.Timers, TickPhase.Updates, TickPhase.Acquisition, TickPhase.Turrets, TickPhase.FiringTrackers, TickPhase.Events, TickPhase.Sales,
        TickPhase.Destroys, TickPhase.DelayedDamage, TickPhase.Spawns, TickPhase.SpawnEvents, TickPhase.Vision, TickPhase.VisionHash, TickPhase.Hash,
    ];

    /// <summary>Writes down what the world reports; a phase that ends without having started, or an update outside the update phase, is a failure.</summary>
    private sealed class PhaseLog : ITickPhaseObserver
    {
        private TickPhase? _open;
        private SimModule? _openUpdate;
        public List<TickPhase> Phases { get; } = [];
        public long Updates { get; private set; }

        public void PhaseStarted(TickPhase phase)
        {
            Check.That(_open is null, $"Phase {phase} starts while {_open} is still open.");
            _open = phase;
        }

        public void PhaseEnded(TickPhase phase)
        {
            Check.That(_open == phase, $"Phase {phase} ends, but {_open?.ToString() ?? "none"} is open.");
            Check.That(_openUpdate is null, "A phase ends inside a module update.");
            _open = null;
            Phases.Add(phase);
        }

        public void UpdateStarted(SimModule module)
        {
            Check.That(_open == TickPhase.Updates && _openUpdate is null, "A module update starts outside the update phase or inside another update.");
            _openUpdate = module;
        }

        public void UpdateEnded(SimModule module)
        {
            Check.That(ReferenceEquals(_openUpdate, module), "A module update ends that did not start.");
            _openUpdate = null;
            Updates++;
        }

        /// <summary>Takes the phases seen since the last call.</summary>
        public TickPhase[] Take()
        {
            var seen = Phases.ToArray();
            Phases.Clear();
            return seen;
        }
    }

    public static void TickPhaseObserverWorld()
    {
        Check.That(Enum.GetValues<TickPhase>().Take(WorldPhases.Length).SequenceEqual(WorldPhases), "The enum declares the world's phases first and in step order.");

        // A world without terrain: the short stress replay (spawns, destroys, timers, commands, events).
        var (fixture, replay, _) = ShortReplay();
        var plain = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        var log = new PhaseLog();
        var observedWorld = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        observedWorld.PhaseObserver = log;
        var ticks = 0;
        var observed = ReplayRunner.Run(replay, observedWorld, new ReplayRunOptions
        {
            RecordTickHashes = true,
            AfterTick = (_, _) =>
            {
                ticks++;
                Check.That(log.Take().SequenceEqual(WorldPhases), "Every world phase is reported exactly once per tick, in step order (also the empty vision phases of a world without terrain).");
            },
        });
        Check.That(ticks == plain.TickHashes.Length && ticks > 0, "The observed run executed every tick.");
        Check.That(observed.TickHashes.AsSpan().SequenceEqual(plain.TickHashes), "A world stepped with an observer produces the same tick hashes as without.");
        Check.That(observed.FinalHash == plain.FinalHash && observed.VerifiedCheckpoints == replay.Checkpoints.Length, "Final hash and checkpoints agree with the observer attached.");
        Check.That(log.Updates > 0, "The observer saw module updates at the scheduler's dispatch.");

        // The observer is not state: a snapshot taken with it attached is byte-identical, and a restored world has none.
        var half = replay.Header.EndTick / 2;
        var withObserver = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        withObserver.PhaseObserver = new PhaseLog();
        ReplayRunner.Run(replay, withObserver, new ReplayRunOptions { StopTick = half });
        var without = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        ReplayRunner.Run(replay, without, new ReplayRunOptions { StopTick = half });
        var snapshot = WorldSnapshot.Write(withObserver);
        Check.That(snapshot.AsSpan().SequenceEqual(WorldSnapshot.Write(without)), "The snapshot does not contain the observer.");
        Check.That(WorldSnapshot.Restore(snapshot, fixture.Templates, fixture.Fingerprint).PhaseObserver is null, "A restored world has no observer.");
    }

    public static void TickPhaseObserverSessions()
    {
        // A skirmish on a terrain: the AIs think, the vision is refreshed every third tick.
        var skirmishFixture = new SyntheticFixture(files: SkirmishFiles());
        var skirmishMap = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        var skirmishTerrain = TerrainMap.Build(skirmishMap, "skirmish");
        var participants = ReferenceMatchScenario.Participants(skirmishFixture.Templates, out _);
        var options = new SkirmishMatchOptions(0xC0FFEE06, 7, 3_000, 60, 500, 500, 0);
        SkirmishMatchSession Match() => SkirmishMatchSession.Start(options, skirmishFixture.Templates, skirmishFixture.Fingerprint, skirmishTerrain, skirmishMap.Objects, [.. participants.Select(participant => new SkirmishSlot(participant))]);
        TickPhase[] skirmishOrder = [TickPhase.SkirmishAi, ..WorldPhases, TickPhase.SkirmishVictory];
        var plainMatch = Match();
        var observedMatch = Match();
        var matchLog = new PhaseLog();
        observedMatch.World.PhaseObserver = matchLog;
        while (!plainMatch.IsOver)
        {
            Check.That(!observedMatch.IsOver, "The observed match ends when the plain one does.");
            var expected = plainMatch.Step();
            var actual = observedMatch.Step();
            Check.That(expected.StateHash == actual.StateHash, $"Skirmish tick {expected.Tick}: same hash with and without the observer.");
            Check.That(matchLog.Take().SequenceEqual(skirmishOrder), $"Skirmish tick {expected.Tick}: the AI part, the world phases and the victory check, each once and in order.");
        }
        Check.That(observedMatch.IsOver && observedMatch.World.StateHash == plainMatch.World.StateHash && observedMatch.Winner == plainMatch.Winner, "Both matches end in the same state.");
        Check.That(observedMatch.ToReplay().Commands.SequenceEqual(plainMatch.ToReplay().Commands), "The AIs issued the same commands.");
        Check.That(matchLog.Updates > 0, "The skirmish ran module updates under the observer.");

        // A mission: scripts before the world step, the session's event tail after it.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Orders", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("UNIT_MOVE_TOWARDS_NEAREST_OBJECT_TYPE", Scr.Unit("Hiker"), Scr.Type("AlphaBunker"), Scr.Area("LeftArea"))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Hiker"), Placed("AlphaBunker", 150m, 100m, "teamB", "Bunker")]);
        TickPhase[] missionOrder = [TickPhase.MissionScripts, ..WorldPhases, TickPhase.MissionEvents];
        var plainMission = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var observedMission = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var missionLog = new PhaseLog();
        observedMission.World.PhaseObserver = missionLog;
        for (var tick = 0; tick < 400; tick++)
        {
            var expected = plainMission.Step();
            var actual = observedMission.Step();
            Check.That(expected.StateHash == actual.StateHash, $"Mission tick {tick}: same world hash with and without the observer.");
            Check.That(missionLog.Take().SequenceEqual(missionOrder), $"Mission tick {tick}: scripts, the world phases and the event tail, each once and in order.");
        }
        Check.That(observedMission.StateHash() == plainMission.StateHash(), "World and script state agree at the end.");
        Check.That(MissionSnapshot.Write(observedMission).AsSpan().SequenceEqual(MissionSnapshot.Write(plainMission)), "The mission snapshot does not contain the observer.");
    }
}

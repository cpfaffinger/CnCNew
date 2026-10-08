using System.Globalization;
using CnCNew.Formats;
using CnCNew.Game.Campaign;
using CnCNew.SimTool.Manifests;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// <c>reference-manifest results</c>: for the runs of the manifest, the quantities of a run that are not hashes - who won and when, what every player owns
/// and has in the bank at the end, how many commands of each type were given and how many events of each kind happened. Hashes say that two runs are the
/// same; these say how far apart two runs are that are meant to differ (a change of the numeric resolution, of the tick rate). Amounts are written in
/// units, never as raw values, so that the output of two resolutions can be laid side by side.
/// </summary>
internal static class ResultQuantities
{
    /// <summary>Counts the events of a run by kind.</summary>
    private sealed class Tally
    {
        private readonly long[] _events = new long[256];

        public void Add(IReadOnlyList<SimEvent> events)
        {
            for (var index = 0; index < events.Count; index++) _events[(byte)events[index].Kind]++;
        }

        public ManifestObject Events()
        {
            var values = new ManifestObject();
            for (var kind = 0; kind < _events.Length; kind++)
                if (_events[kind] != 0) values.Add(((SimEventKind)kind).ToString(), _events[kind]);
            return values;
        }
    }

    /// <summary>Per player of the world: the objects it owns and its money in units with two decimals.</summary>
    private static ManifestObject Players(SimulationWorld world)
    {
        var owned = new SortedDictionary<int, long>();
        foreach (var entity in world.Entities) owned[entity.Owner.Value] = owned.GetValueOrDefault(entity.Owner.Value) + 1;
        var names = world.Configuration.Players.ToDictionary(player => player.Id.Value, player => player.Faction ?? player.Name);
        var values = new ManifestObject();
        foreach (var id in world.PlayerIds.Select(id => id.Value).Union(owned.Keys).Order())
            values.Add(string.Create(CultureInfo.InvariantCulture, $"player{id:D2}"), new ManifestObject()
                .Add("name", names.GetValueOrDefault(id, string.Empty))
                .Add("objects", owned.GetValueOrDefault(id))
                .Add("money", decimal.Round(world.MoneyOf(new Simulation.Ids.PlayerId(id)).ToDecimal(), 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture)));
        return values;
    }

    private static ManifestObject Commands(ReplayFile recorded)
    {
        var values = new ManifestObject();
        foreach (var group in recorded.Commands.GroupBy(command => command.GetType().Name, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
            values.Add(group.Key, group.LongCount());
        return values;
    }

    private static void AddOutcome(ManifestObject values, string? winner, long victoryTick)
    {
        values.Add("outcome", winner is null ? "stalemate" : "winner");
        if (winner is null) return;
        values.Add("winner", winner).Add("victoryTick", victoryTick);
    }

    /// <summary><c>--commands &lt;dir&gt;</c>: where the command streams of the recorded runs are written, one text file per run; null: nowhere.</summary>
    public static string? CommandsDirectory { get; set; }

    [ThreadStatic] private static string? _run;

    /// <summary>Names the run the calling worker computes next (the file name of its command stream).</summary>
    public static void Begin(string run) => _run = run;

    /// <summary>
    /// The command stream as text, one command per line in replay order: tick, player, type and every field. Positions and amounts are rounded to whole
    /// units, so that the streams of two numeric resolutions can be compared line by line: the first line that differs is the first decision of the driver
    /// (an AI, the scenario) that came out differently, not a difference in the last digit. The AI match records its commands through the same replay.
    /// </summary>
    private static void WriteCommands(ReplayFile recorded)
    {
        if (CommandsDirectory is null || _run is null) return;
        var text = new System.Text.StringBuilder();
        foreach (var command in recorded.Commands)
        {
            text.Append(CultureInfo.InvariantCulture, $"{command.Tick} p{command.Player.Value} {command.GetType().Name}");
            foreach (var property in command.GetType().GetProperties().OrderBy(property => property.MetadataToken))
            {
                if (property.Name is "Tick" or "Player" or "Sequence" or "EqualityContract") continue;
                var value = property.GetValue(command);
                var shown = value switch
                {
                    Simulation.Numerics.Fixed number => number.Round().ToString(CultureInfo.InvariantCulture),
                    Simulation.Ids.EntityId id => id.Value.ToString(CultureInfo.InvariantCulture),
                    Simulation.Ids.PlayerId id => id.Value.ToString(CultureInfo.InvariantCulture),
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    null => "-",
                    _ => value.ToString() ?? string.Empty,
                };
                // A rounded amount is marked with '~': a comparison may allow it a unit of difference.
                text.Append(' ').Append(property.Name).Append(value is Simulation.Numerics.Fixed ? '~' : '=').Append(shown);
            }
            text.Append('\n');
        }
        Directory.CreateDirectory(CommandsDirectory);
        File.WriteAllText(Path.Combine(CommandsDirectory, _run + ".commands.txt"), text.ToString());
    }

    /// <summary>One fresh run of a recorded replay with the tally attached.</summary>
    private static ManifestObject Replayed(ReplayFile recorded, TemplateCatalog catalog, TerrainMap? terrain)
    {
        WriteCommands(recorded);
        var replay = ReplayFormat.Read(ReplayFormat.Write(recorded));
        var world = ReplayRunner.CreateWorld(replay.Header, catalog, terrain);
        var tally = new Tally();
        var run = ReplayRunner.Run(replay, world, new ReplayRunOptions { AfterTick = (_, tick) => tally.Add(tick.Events) });
        return new ManifestObject()
            .Add("endTick", replay.Header.EndTick).Add("rejectedCommands", run.RejectedCommands).Add("objects", world.Entities.Count)
            .Add("commandCount", recorded.Commands.Length).Add("commands", Commands(recorded)).Add("events", tally.Events()).Add("players", Players(world));
    }

    public static ManifestObject ReferenceMatch(SimContext context, ReferenceMatchOptions options)
    {
        var scenario = new ReferenceMatchScenario(options);
        var values = Replayed(scenario.Record(context.Catalog, context.Fingerprint), context.Catalog, null);
        AddOutcome(values, scenario.Winner is null ? null : scenario.WinnerFaction, scenario.VictoryTick);
        return values.Add("peakLivingObjects", scenario.PeakAlive).Add("scenarioShots", scenario.Shots);
    }

    public static ManifestObject Stress(StressOptions options)
    {
        var fixture = new SyntheticFixture(ToolRate.Current);
        var scenario = new StressScenario(options);
        return Replayed(scenario.Record(fixture), fixture.Templates, null).Add("outcome", "none").Add("peakLivingObjects", scenario.PeakAlive);
    }

    public static ManifestObject AiMatch(SimContext context, string mapPath, string[] factions, SkirmishMatchOptions options, Func<SimContext, IReadOnlyList<string>, System.Collections.Immutable.ImmutableArray<MatchParticipant>> participants)
    {
        using var stream = context.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        var scenario = new SkirmishMatchScenario(options);
        var values = Replayed(scenario.Record(context.Catalog, context.Fingerprint, terrain, map.Objects, participants(context, factions)), context.Catalog, terrain);
        AddOutcome(values, scenario.Winner is { } winner ? scenario.Players.First(player => player.Id == winner).Faction : null, scenario.VictoryTick);
        return values;
    }

    public static ManifestObject SweepMap(SimContext context, string path, SkirmishMatchOptions options)
    {
        try
        {
            using var stream = context.Content.OpenRead(path);
            var map = SageMapDecoder.Decode(stream);
            var terrain = TerrainMap.Build(map, path);
            if (terrain.StartPositions.Length < 2) return new ManifestObject().Add("status", terrain.StartPositions.Length == 0 ? "no-start" : "one-start");
            var chosen = ReferenceMatchScenario.Participants(context.Catalog, out _).Take(2).ToArray();
            var session = SkirmishMatchSession.Start(options, context.Catalog, context.Fingerprint, terrain, map.Objects, [..chosen.Select(participant => new SkirmishSlot(participant))]);
            var tally = new Tally();
            while (!session.IsOver) tally.Add(session.Step().Events);
            var values = new ManifestObject().Add("status", "ended").Add("endTick", session.World.Tick).Add("objects", session.World.Entities.Count)
                .Add("events", tally.Events()).Add("players", Players(session.World));
            AddOutcome(values, session.Winner is { } winner ? session.Players.First(player => player.Id == winner).Faction : null, session.VictoryTick);
            return values;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new ManifestObject().Add("status", "failed").Add("error", $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public static ManifestObject Mission(SimContext context, string mapPath, bool challenge, string faction, long ticks)
    {
        using var stream = context.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        var options = MissionOptions.Standard with { MaxTicks = 0, HumanSideName = challenge ? ChallengeSides.HumanSideFor(map) : string.Empty, HumanFaction = challenge ? faction : string.Empty };
        var session = MissionSession.Start(options, context.Catalog, context.Fingerprint, terrain, map);
        var tally = new Tally();
        var chain = StateHash.Seed;
        while (session.World.Tick < ticks && !session.IsOver)
        {
            var result = session.Step();
            chain = StateHash.Mix(chain, result.StateHash);
            tally.Add(result.Events);
        }
        var report = MissionScenario.Report(session, terrain.Name, chain);
        return new ManifestObject()
            .Add("endTick", report.Ticks).Add("outcome", report.Outcome.ToString().ToLowerInvariant()).Add("outcomeTick", report.OutcomeTick).Add("outcomeScript", report.OutcomeScript)
            .Add("ended", report.Ended).Add("objects", report.Entities).Add("executedScripts", report.ExecutedScripts).Add("executedActions", report.ExecutedActions)
            .Add("forwardedActions", report.ForwardedActions).Add("shots", report.Shots).Add("missingActionHits", report.MissingActionTotal).Add("missingConditionHits", report.MissingConditionTotal)
            .Add("events", tally.Events()).Add("players", Players(session.World));
    }
}

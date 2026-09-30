using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

/// <param name="MaxTicks">Hard stop when nobody has won by then.</param>
/// <param name="GraceTicks">Ticks recorded after the winner is known.</param>
/// <param name="StartMoney">Cash every player starts with, credited by the system at tick 0; the skirmish setup screen (<c>GameInfo</c>) sets it, the faction data holds none.</param>
/// <param name="SciencePointInterval">Every this many ticks the system credits each living player one science point (0: never); stand-in for the rank system, which does not exist.</param>
public sealed record SkirmishMatchOptions(uint WorldSeed, uint AiSeed, long MaxTicks, long GraceTicks, int CheckpointInterval, int StartMoney, int SciencePointInterval)
{
    /// <summary>One hour of game time.</summary>
    public static SkirmishMatchOptions Standard { get; } = new(0xC0FFEE06, 0x5EED0006, 108_000, 300, 1_000, 10_000, 2_700);
}

/// <summary>
/// A headless match of skirmish AIs on a real or synthetic terrain: every participant is a computer slot of a
/// <see cref="SkirmishMatchSession"/> that is stepped to its end. All commands, rejected ones included, are recorded
/// as a replay that any run must reproduce hash for hash.
/// </summary>
public sealed class SkirmishMatchScenario
{
    public const string FixtureId = SkirmishMatchSession.FixtureId;

    private readonly SkirmishMatchOptions _options;

    public SkirmishMatchScenario(SkirmishMatchOptions options) => _options = options;

    public ImmutableArray<PlayerConfiguration> Players { get; private set; } = [];
    public ImmutableArray<SkirmishAi> Ais { get; private set; } = [];
    public PlayerId? Winner { get; private set; }
    public long VictoryTick { get; private set; } = -1;
    public long EndTick { get; private set; }
    public int WarehousesSpawned { get; private set; }
    public ImmutableArray<string> Notes { get; private set; } = [];
    public ImmutableArray<(string Faction, long Money, int Objects, bool Defeated)> Finals { get; private set; } = [];

    public static string FamilyPrefix(FactionInfo faction) => SkirmishMatchSession.FamilyPrefix(faction);

    /// <summary>Plays the match and returns the recorded replay (without checkpoints).</summary>
    public ReplayFile Record(TemplateCatalog catalog, string fingerprint, TerrainMap terrain, ImmutableArray<MapObject> mapObjects, ImmutableArray<MatchParticipant> participants)
    {
        var session = SkirmishMatchSession.Start(_options, catalog, fingerprint, terrain, mapObjects, [..participants.Select(participant => new SkirmishSlot(participant))]);
        while (!session.IsOver) session.Step();
        var world = session.World;
        Players = session.Players;
        Ais = [..session.Ais.Select(ai => ai!)];
        Winner = session.Winner;
        VictoryTick = session.VictoryTick;
        EndTick = session.EndTick;
        WarehousesSpawned = session.WarehousesSpawned;
        Notes = session.Notes;
        Finals = [..Players.Select(player => (player.Faction ?? string.Empty, (long)world.MoneyOf(player.Id).Floor(),
            world.Entities.Count(entity => entity.Owner == player.Id && !entity.EffectivelyDead), world.IsPlayerDefeated(player.Id)))];
        return session.ToReplay();
    }
}

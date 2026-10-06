using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Live;

/// <param name="MapPath">Virtual path of the <c>.map</c> file.</param>
/// <param name="HumanFaction">Faction name of the human slot (player 1).</param>
/// <param name="Opponents">One computer slot per entry, in order (players 2..).</param>
public sealed record MatchSetup(string MapPath, string HumanFaction, ImmutableArray<string> Opponents, SkirmishMatchOptions Options)
{
    /// <summary>0 keeps the seeds of <see cref="Options"/>.</summary>
    public int Seed { get; init; }

    /// <summary>The human slot is played by the computer player (demos, captures, profiling); the viewer stays player 1.</summary>
    public bool AutoPlay { get; init; }
}

/// <summary>
/// A running skirmish: the session (replayable) behind a <see cref="LiveGame"/>. The human is player 1, the opponents are computer slots.
/// </summary>
public sealed class LiveMatch : LiveGame
{

    private LiveMatch(GameData data, MatchSetup setup, SkirmishMatchSession session, TerrainMap terrain, MapData map, uint fxSeed, Vector2 viewport)
        : base(data, session.World, map, setup.MapPath, terrain, session.Players, new PlayerId(1), fxSeed, viewport,
            new Vector2(CnCNew.Presentation.FixedConvert.ToSingle(terrain.StartPositions[0].X), CnCNew.Presentation.FixedConvert.ToSingle(terrain.StartPositions[0].Y)))
    {
        Setup = setup;
        Session = session;
    }

    public MatchSetup Setup { get; }
    public SkirmishMatchSession Session { get; }

    public override string HumanFaction => Setup.HumanFaction;
    public override bool IsOver => Session.IsOver;
    public override PlayerId? Winner => Session.Winner;
    public override ImmutableArray<PlayerConfiguration> Players => Session.Players;

    /// <summary>Command sink of the human player: everything the human changes goes through it, so <see cref="SkirmishMatchSession.ToReplay"/> reproduces the match.</summary>
    protected override ICommandSink CreateCommandSink() => new SkirmishSessionCommandSink(Session, Human);

    protected override TickResult StepSimulation() => Session.Step();

    public static LiveMatch Create(GameData data, MatchSetup setup, Vector2 viewport)
    {
        MapData map;
        using (var stream = data.Content.OpenRead(setup.MapPath)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, setup.MapPath);

        MatchParticipant Find(string name) => data.Factions.FirstOrDefault(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown or unplayable faction '{name}'.");
        var slots = ImmutableArray.CreateBuilder<SkirmishSlot>();
        slots.Add(new SkirmishSlot(Find(setup.HumanFaction), IsHuman: !setup.AutoPlay));
        foreach (var opponent in setup.Opponents) slots.Add(new SkirmishSlot(Find(opponent)));
        var options = setup.Options;
        if (setup.Seed != 0) options = options with { AiSeed = unchecked((uint)setup.Seed), WorldSeed = unchecked((uint)setup.Seed) ^ 0xC0FFEE06 };
        var session = SkirmishMatchSession.Start(options, data.Templates, data.Fingerprint, terrain, map.Objects, slots.ToImmutable());
        return new LiveMatch(data, setup, session, terrain, map, options.WorldSeed, viewport);
    }
}

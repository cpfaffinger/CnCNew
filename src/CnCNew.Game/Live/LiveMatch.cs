using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Live;

/// <summary>Who plays a computer slot: the simulation's own AI (the default) or the original one with its script library, and for that one the army (easy, medium, hard).</summary>
public readonly record struct MatchAi(SkirmishAiKind Kind, ScriptDifficulty Difficulty = ScriptDifficulty.Normal)
{
    public static MatchAi Builtin { get; } = new(SkirmishAiKind.Builtin);

    /// <summary><c>builtin</c> or <c>original</c>, as the tools and the client spell it.</summary>
    public static SkirmishAiKind ParseKind(string text) => text.ToLowerInvariant() switch
    {
        "builtin" => SkirmishAiKind.Builtin,
        "original" => SkirmishAiKind.Original,
        _ => throw new ArgumentException($"An AI is 'builtin' or 'original', not '{text}'."),
    };

    /// <summary><c>easy</c>, <c>medium</c> (or <c>normal</c>) or <c>hard</c> (or <c>brutal</c>).</summary>
    public static ScriptDifficulty ParseDifficulty(string text) => text.ToLowerInvariant() switch
    {
        "easy" => ScriptDifficulty.Easy,
        "medium" or "normal" => ScriptDifficulty.Normal,
        "hard" or "brutal" => ScriptDifficulty.Hard,
        _ => throw new ArgumentException($"A difficulty is 'easy', 'medium' or 'hard', not '{text}'."),
    };

    public override string ToString() => Kind == SkirmishAiKind.Builtin ? "builtin" : "original/" + Difficulty switch { ScriptDifficulty.Easy => "easy", ScriptDifficulty.Hard => "hard", _ => "medium" };
}

/// <param name="MapPath">Virtual path of the <c>.map</c> file.</param>
/// <param name="HumanFaction">Faction name of the human slot (player 1).</param>
/// <param name="Opponents">One computer slot per entry, in order (players 2..).</param>
public sealed record MatchSetup(string MapPath, string HumanFaction, ImmutableArray<string> Opponents, SkirmishMatchOptions Options)
{
    /// <summary>0 keeps the seeds of <see cref="Options"/>.</summary>
    public int Seed { get; init; }

    /// <summary>The human slot is played by the computer player (demos, captures, profiling); the viewer stays player 1.</summary>
    public bool AutoPlay { get; init; }

    /// <summary>
    /// Nobody plays (docs/SPECTATOR-MODE.md): every slot is a computer player and the viewer only watches. Orders of the viewer end in
    /// <see cref="CnCNew.Game.Spectator.SpectatorCommandSink"/> and never reach the session.
    /// </summary>
    public bool Spectator { get; init; }

    /// <summary>
    /// The AI of each slot in slot order - entry 0 is the first slot (used when the computer plays it: <see cref="AutoPlay"/>, <see cref="Spectator"/>), entry <c>i</c> opponent <c>i</c>.
    /// Missing entries are the simulation's own AI. Empty (the default): every computer slot is the simulation's own AI in a <see cref="SkirmishMatchSession"/>, exactly as before.
    /// As soon as one slot names the original AI the match runs in a <see cref="ScriptedSkirmishSession"/> (docs/SKIRMISH-AI-ORIGINAL.md).
    /// </summary>
    public ImmutableArray<MatchAi> Ai { get; init; } = [];

    /// <summary>The AI of a slot (<see cref="Ai"/>).</summary>
    public MatchAi AiOf(int slot) => !Ai.IsDefault && slot < Ai.Length ? Ai[slot] : MatchAi.Builtin;

    /// <summary>Whether any slot is played by the original AI, i.e. whether the match is a scripted skirmish.</summary>
    public bool UsesOriginalAi => !Ai.IsDefaultOrEmpty && Ai.Any(ai => ai.Kind == SkirmishAiKind.Original);
}

/// <summary>
/// A running skirmish behind a <see cref="LiveGame"/>. The human is player 1, the opponents are computer slots. With the simulation's own AI it is a
/// <see cref="SkirmishMatchSession"/> (replayable); with the original AI in any slot a <see cref="ScriptedSkirmishSession"/>, whose truth is its state.
/// </summary>
public sealed class LiveMatch : LiveGame
{
    private readonly SkirmishMatchSession? _session;

    private LiveMatch(GameData data, MatchSetup setup, SkirmishMatchSession session, TerrainMap terrain, MapData map, uint fxSeed, Vector2 viewport)
        : base(data, session.World, map, setup.MapPath, terrain, session.Players, new PlayerId(1), fxSeed, viewport,
            new Vector2(CnCNew.Presentation.FixedConvert.ToSingle(terrain.StartPositions[0].X), CnCNew.Presentation.FixedConvert.ToSingle(terrain.StartPositions[0].Y)))
    {
        Setup = setup;
        _session = session;
    }

    private LiveMatch(GameData data, MatchSetup setup, ScriptedSkirmishSession session, TerrainMap terrain, MapData map, uint fxSeed, Vector2 viewport)
        : base(data, session.World, map, setup.MapPath, terrain, session.Players, new PlayerId(1), fxSeed, viewport,
            new Vector2(CnCNew.Presentation.FixedConvert.ToSingle(terrain.StartPositions[0].X), CnCNew.Presentation.FixedConvert.ToSingle(terrain.StartPositions[0].Y)))
    {
        Setup = setup;
        Scripted = session;
        _scriptedPlayers = session.Players;
    }

    private readonly ImmutableArray<PlayerConfiguration> _scriptedPlayers;

    public MatchSetup Setup { get; }

    /// <summary>The session of a match of the simulation's own AI. A match with the original AI has none: ask <see cref="Scripted"/> (or the members of this class, which serve both).</summary>
    public SkirmishMatchSession Session => _session ?? throw new InvalidOperationException("This match runs the original AI in a ScriptedSkirmishSession; it has no SkirmishMatchSession.");

    /// <summary>The session of a match with the original AI in some slot, else <see langword="null"/>.</summary>
    public ScriptedSkirmishSession? Scripted { get; }

    public override string HumanFaction => Setup.HumanFaction;
    public override bool IsOver => Scripted?.IsOver ?? Session.IsOver;
    public override PlayerId? Winner => Scripted is { } scripted ? scripted.Winner : Session.Winner;
    public override ImmutableArray<PlayerConfiguration> Players => Scripted is null ? Session.Players : _scriptedPlayers;

    /// <summary>The tick the winner was decided at, or -1.</summary>
    public long VictoryTick => Scripted?.VictoryTick ?? Session.VictoryTick;

    /// <summary>The tick the match ended or will end at the latest (the budget of the session).</summary>
    public long EndTick => Scripted?.EndTick ?? Session.EndTick;

    /// <summary>
    /// Every command submitted so far in the order of submission, whoever plays the slots: what an observer follows (the spectator's camera of a computer player). For a match with
    /// the original AI the session keeps them from the moment the match was created.
    /// </summary>
    public IReadOnlyList<SimCommand> RecordedCommands => Scripted?.RecordedCommands ?? Session.RecordedCommands;

    /// <summary>Executions of script types without implementation so far (a match with the original AI; 0 for the simulation's own AI, which runs no scripts).</summary>
    public long MissingScriptTypeHits => Scripted is { } scripted ? scripted.Engine.MissingActionHits.Values.Sum() + scripted.Engine.MissingConditionHits.Values.Sum() : 0;

    /// <summary>Command sink of the human player: everything the human changes goes through the session (for the simulation's own AI so that <see cref="SkirmishMatchSession.ToReplay"/> reproduces the match).</summary>
    protected override ICommandSink CreateCommandSink()
    {
        if (Setup.Spectator) return SpectatorSink = new CnCNew.Game.Spectator.SpectatorCommandSink();
        return Scripted is { } scripted ? new ScriptedSink(scripted, Human) : new SkirmishSessionCommandSink(Session, Human);
    }

    private sealed class ScriptedSink(ScriptedSkirmishSession session, PlayerId player) : ICommandSink
    {
        public SubmitResult Issue(Func<ulong, long, SimCommand> make) => session.Issue(player, make);
    }

    /// <summary>The refusing sink of a spectator's match once <see cref="LiveGame.Commands"/> was asked for; null in a match somebody plays.</summary>
    public CnCNew.Game.Spectator.SpectatorCommandSink? SpectatorSink { get; private set; }

    protected override TickResult StepSimulation() => Scripted?.Step() ?? Session.Step();

    public static LiveMatch Create(GameData data, MatchSetup setup, Vector2 viewport)
    {
        MapData map;
        using (var stream = data.Content.OpenRead(setup.MapPath)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, setup.MapPath);

        MatchParticipant Find(string name) => data.Factions.FirstOrDefault(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown or unplayable faction '{name}'.");
        var humanPlays = !setup.AutoPlay && !setup.Spectator;
        if (setup.UsesOriginalAi) return CreateScripted(data, setup, viewport, map, terrain, Find, humanPlays);

        var slots = ImmutableArray.CreateBuilder<SkirmishSlot>();
        slots.Add(new SkirmishSlot(Find(setup.HumanFaction), IsHuman: humanPlays));
        foreach (var opponent in setup.Opponents) slots.Add(new SkirmishSlot(Find(opponent)));
        var options = setup.Options;
        if (setup.Seed != 0) options = options with { AiSeed = unchecked((uint)setup.Seed), WorldSeed = unchecked((uint)setup.Seed) ^ 0xC0FFEE06 };
        var session = SkirmishMatchSession.Start(options, data.Templates, data.Fingerprint, terrain, map.Objects, slots.ToImmutable());
        return new LiveMatch(data, setup, session, terrain, map, options.WorldSeed, viewport);
    }

    /// <summary>
    /// The match as a scripted skirmish: the script library and the AI data come from the content (the game layer may read it, the simulation may not); budget, start money and the
    /// science stand-in are the ones of the setup's options, so that a match of either AI is set up alike.
    /// </summary>
    private static LiveMatch CreateScripted(GameData data, MatchSetup setup, Vector2 viewport, MapData map, TerrainMap terrain, Func<string, MatchParticipant> find, bool humanPlays)
    {
        var original = data.LoadOriginalAi();
        var slots = ImmutableArray.CreateBuilder<ScriptedSkirmishSlot>();
        var first = setup.AiOf(0);
        slots.Add(new ScriptedSkirmishSlot(find(setup.HumanFaction), first.Difficulty, IsHuman: humanPlays, Kind: first.Kind));
        for (var index = 0; index < setup.Opponents.Length; index++)
        {
            var ai = setup.AiOf(index + 1);
            slots.Add(new ScriptedSkirmishSlot(find(setup.Opponents[index]), ai.Difficulty, Kind: ai.Kind));
        }
        var standard = ScriptedSkirmishOptions.Standard;
        var seed = setup.Seed != 0 ? unchecked((uint)setup.Seed) ^ 0xC0FFEE06 : setup.Options.WorldSeed;
        var options = standard with
        {
            Seed = seed, MaxTicks = setup.Options.MaxTicks, StartMoney = setup.Options.StartMoney,
            // Both are logic frames of the original in either set of options (SkirmishMatchOptions keeps the name "ticks" for its callers).
            GraceFrames = setup.Options.GraceTicks, SciencePointIntervalFrames = setup.Options.SciencePointInterval,
        };
        var session = ScriptedSkirmishSession.Start(options, data.Templates, data.Fingerprint, terrain, map, original.Library, original.Data, slots.ToImmutable());
        session.RecordCommands();
        return new LiveMatch(data, setup, session, terrain, map, seed, viewport);
    }
}

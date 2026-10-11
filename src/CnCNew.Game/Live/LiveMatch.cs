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

/// <param name="Team">Alliance number (0: none).</param>
/// <param name="Color">Index of the player colour, -1 for a random one.</param>
/// <param name="StartPosition">Index of the map's start position, -1 for a random one.</param>
public readonly record struct MatchSlotDetails(int Team = 0, int Color = -1, int StartPosition = -1);

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

    /// <summary>
    /// Team, colour and start position per slot in slot order (entry 0 the first slot, entry <c>i</c> opponent <c>i</c>); a missing entry is "no team, random colour, the slot's
    /// own start position". They go into the <see cref="MatchDescription"/> as they are.
    /// </summary>
    public ImmutableArray<MatchSlotDetails> Details { get; init; } = [];

    /// <summary>The details of a slot (<see cref="Details"/>).</summary>
    public MatchSlotDetails DetailsOf(int slot) => !Details.IsDefault && slot < Details.Length ? Details[slot] : new MatchSlotDetails(StartPosition: slot);

    /// <summary>A faction the seed of the match chooses: what the setup screen calls "Random".</summary>
    public const string RandomFaction = "";

    /// <summary>
    /// The match as the simulation is given it. The first slot is a human or, when the computer plays it, a computer slot; who watches locally is not part of it. An AI of the
    /// original kind is described as a computer slot with its strength - which session plays it is this layer's choice (<see cref="UsesOriginalAi"/>).
    /// </summary>
    public MatchDescription Describe(GameData data, TerrainMap terrain)
    {
        var options = Options;
        if (Seed != 0) options = options with { AiSeed = unchecked((uint)Seed), WorldSeed = unchecked((uint)Seed) ^ 0xC0FFEE06 };
        var slots = ImmutableArray.CreateBuilder<MatchSlotDescription>();
        for (var slot = 0; slot <= Opponents.Length; slot++)
        {
            var details = DetailsOf(slot);
            var human = slot == 0 && !AutoPlay && !Spectator;
            var ai = AiOf(slot);
            slots.Add(new MatchSlotDescription(human ? MatchSlotKind.Human : MatchSlotKind.Computer, slot == 0 ? HumanFaction : Opponents[slot - 1], details.Team, details.Color, details.StartPosition,
                ai.Kind == SkirmishAiKind.Original ? ai.Difficulty switch { ScriptDifficulty.Easy => 0, ScriptDifficulty.Hard => 2, _ => 1 } : MatchSlotDescription.MediumStrength));
        }
        return new MatchDescription(data.Rate.TicksPerSecond, MapPath, terrain.Fingerprint, data.Fingerprint.ToUpperInvariant(), options.WorldSeed, options.AiSeed, options.MaxTicks, options.StartMoney,
            options.GraceTicks, options.SciencePointInterval, slots.ToImmutable());
    }

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

    private LiveMatch(GameData data, MatchSetup setup, ResolvedMatch match, SkirmishMatchSession session, TerrainMap terrain, MapData map, uint fxSeed, Vector2 viewport)
        : base(data, session.World, map, setup.MapPath, terrain, session.Players, new PlayerId(1), fxSeed, viewport, HomeOf(match, terrain))
    {
        Setup = setup;
        Match = match;
        _session = session;
    }

    private LiveMatch(GameData data, MatchSetup setup, ResolvedMatch match, ScriptedSkirmishSession session, TerrainMap terrain, MapData map, uint fxSeed, Vector2 viewport)
        : base(data, session.World, map, setup.MapPath, terrain, session.Players, new PlayerId(1), fxSeed, viewport, HomeOf(match, terrain))
    {
        Setup = setup;
        Match = match;
        Scripted = session;
        _scriptedPlayers = session.Players;
    }

    /// <summary>Where the local viewer starts to look: the start position of the first slot.</summary>
    private static Vector2 HomeOf(ResolvedMatch match, TerrainMap terrain)
    {
        var start = terrain.StartPositions[match.Slots[0].StartPosition];
        return new Vector2(CnCNew.Presentation.FixedConvert.ToSingle(start.X), CnCNew.Presentation.FixedConvert.ToSingle(start.Y));
    }

    private readonly ImmutableArray<PlayerConfiguration> _scriptedPlayers;

    public MatchSetup Setup { get; }

    /// <summary>The description of the match with everything "random" decided (factions, start positions, colours); the same on every machine that holds the description.</summary>
    public ResolvedMatch Match { get; }

    /// <summary>The description the match was built from.</summary>
    public MatchDescription Description => Match.Description;

    /// <summary>The session of a match of the simulation's own AI. A match with the original AI has none: ask <see cref="Scripted"/> (or the members of this class, which serve both).</summary>
    public SkirmishMatchSession Session => _session ?? throw new InvalidOperationException("This match runs the original AI in a ScriptedSkirmishSession; it has no SkirmishMatchSession.");

    /// <summary>The session of a match with the original AI in some slot, else <see langword="null"/>.</summary>
    public ScriptedSkirmishSession? Scripted { get; }

    public override string HumanFaction => Match.Slots[0].Participant.Faction.Name;
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
    protected override ICommandSink CreateCommandSink() => Setup.Spectator ? SpectatorSink = new CnCNew.Game.Spectator.SpectatorCommandSink() : base.CreateCommandSink();

    protected override SubmitResult IssueNow(PlayerId player, Func<ulong, long, SimCommand> make) => Scripted?.Issue(player, make) ?? Session.Issue(player, make);

    /// <summary>The refusing sink of a spectator's match once <see cref="LiveGame.Commands"/> was asked for; null in a match somebody plays.</summary>
    public CnCNew.Game.Spectator.SpectatorCommandSink? SpectatorSink { get; private set; }

    protected override TickResult StepSimulation() => Scripted?.Step() ?? Session.Step();

    public static LiveMatch Create(GameData data, MatchSetup setup, Vector2 viewport)
    {
        MapData map;
        using (var stream = data.Content.OpenRead(setup.MapPath)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, setup.MapPath);

        var humanPlays = !setup.AutoPlay && !setup.Spectator;
        var description = setup.Describe(data, terrain);
        var match = description.Resolve(data.Templates, terrain, data.Fingerprint);
        if (setup.UsesOriginalAi) return CreateScripted(data, setup, viewport, map, terrain, match, humanPlays);
        var session = SkirmishMatchSession.Start(description, data.Templates, terrain, map.Objects, setup.Options.CheckpointInterval);
        return new LiveMatch(data, setup, match, session, terrain, map, description.Seed, viewport);
    }

    /// <summary>
    /// The running match as a savegame (<see cref="CnCNew.Game.Save.SkirmishSaveGame"/>): the session state with every computer player, the match description and the recording so
    /// far. What the viewer sees (camera, selection, effects) is not part of it. A match of the original AI with a slot played by the built-in one cannot be saved.
    /// </summary>
    public byte[] Save(string title)
    {
        var viewer = Setup.Spectator ? CnCNew.Game.Save.SkirmishViewer.Spectator : Setup.AutoPlay ? CnCNew.Game.Save.SkirmishViewer.AutoPlay : CnCNew.Game.Save.SkirmishViewer.Player;
        var hash = (Scripted?.StateHash() ?? Session.StateHash()).ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        byte[] state;
        try
        {
            state = Scripted?.Save() ?? Session.Save();
        }
        catch (NotSupportedException exception)
        {
            throw new CnCNew.Game.Save.SaveGameException(exception.Message, exception);
        }
        return CnCNew.Game.Save.SkirmishSaveGame.Write(title, Setup.MapPath, World.Tick, World.Rate.TicksPerSecond, World.Configuration.DefinitionFingerprint, hash, state, Scripted is not null, viewer);
    }

    /// <summary>
    /// Continues a saved skirmish: the map of the save is loaded, the session restored (the simulation checks definitions, terrain and its own state hash) and a fresh
    /// presentation put in front of it. Throws <see cref="CnCNew.Game.Save.SaveGameException"/> with a message fit to show when the file does not load.
    /// </summary>
    public static LiveMatch Restore(GameData data, ReadOnlySpan<byte> save, Vector2 viewport)
    {
        var loaded = CnCNew.Game.Save.SkirmishSaveGame.Load(save, data.Rate.TicksPerSecond, data.Fingerprint);
        var mapPath = loaded.Header.MapPath;
        if (!data.Content.Exists(mapPath)) throw new CnCNew.Game.Save.SaveGameException($"The savegame was played on '{mapPath}', which this game does not have.");
        MapData map;
        using (var stream = data.Content.OpenRead(mapPath)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        try
        {
            LiveMatch match;
            if (loaded.OriginalAi)
            {
                var original = data.LoadOriginalAi();
                var session = ScriptedSkirmishSession.Restore(loaded.State, data.Templates, data.Fingerprint, terrain, map, original.Library, original.Data);
                session.RecordCommands();
                var setup = SetupOf(mapPath, session, loaded.Viewer);
                match = new LiveMatch(data, setup, setup.Describe(data, terrain).Resolve(data.Templates, terrain), session, terrain, map, session.World.Configuration.Seed, viewport);
            }
            else
            {
                var session = SkirmishMatchSession.Restore(loaded.State, data.Templates, data.Fingerprint, terrain);
                match = new LiveMatch(data, SetupOf(session.Description, session.Match, loaded.Viewer), session.Match, session, terrain, map, session.Description.Seed, viewport);
            }
            if (match.World.Tick != loaded.Header.Tick) throw new CnCNew.Game.Save.SaveGameException("The savegame's state does not match its header (the file is damaged).");
            return match;
        }
        catch (CnCNew.Simulation.Snapshots.SnapshotFormatException exception)
        {
            throw new CnCNew.Game.Save.SaveGameException($"The savegame cannot be restored: {exception.Message}", exception);
        }
    }

    /// <summary>The setup a restored match shows itself with: what the description says, and who the viewer was.</summary>
    private static MatchSetup SetupOf(MatchDescription description, ResolvedMatch match, CnCNew.Game.Save.SkirmishViewer viewer)
    {
        var options = new SkirmishMatchOptions(description.Seed, description.AiSeed, description.MaxTicks, description.GraceFrames, 0, description.StartMoney, checked((int)description.SciencePointIntervalFrames));
        return new MatchSetup(description.MapPath, match.Slots[0].Participant.Faction.Name, [..match.Slots.Skip(1).Select(slot => slot.Participant.Faction.Name)], options)
        {
            AutoPlay = viewer == CnCNew.Game.Save.SkirmishViewer.AutoPlay,
            Spectator = viewer == CnCNew.Game.Save.SkirmishViewer.Spectator,
            Details = [..match.Slots.Select(slot => new MatchSlotDetails(slot.Description.Team, slot.Color, slot.StartPosition))],
        };
    }

    private static MatchSetup SetupOf(string mapPath, ScriptedSkirmishSession session, CnCNew.Game.Save.SkirmishViewer viewer)
    {
        var slots = session.Slots;
        var options = SkirmishMatchOptions.Standard with { WorldSeed = session.World.Configuration.Seed, MaxTicks = session.EndTick };
        return new MatchSetup(mapPath, slots[0].Participant.Faction.Name, [..slots.Skip(1).Select(slot => slot.Participant.Faction.Name)], options)
        {
            AutoPlay = viewer == CnCNew.Game.Save.SkirmishViewer.AutoPlay,
            Spectator = viewer == CnCNew.Game.Save.SkirmishViewer.Spectator,
            Ai = [..slots.Select(slot => new MatchAi(slot.Kind, slot.Difficulty))],
            Details = [..slots.Select((slot, index) => new MatchSlotDetails(slot.Team, -1, index))],
        };
    }

    /// <summary>
    /// The match as a scripted skirmish: the script library and the AI data come from the content (the game layer may read it, the simulation may not); budget, start money and the
    /// science stand-in are the ones of the setup's options, so that a match of either AI is set up alike.
    /// </summary>
    private static LiveMatch CreateScripted(GameData data, MatchSetup setup, Vector2 viewport, MapData map, TerrainMap terrain, ResolvedMatch match, bool humanPlays)
    {
        var original = data.LoadOriginalAi();
        // The scripted session places slot i at start position i and knows no teams or colours of its own; the factions are the resolved ones of the description.
        var slots = ImmutableArray.CreateBuilder<ScriptedSkirmishSlot>();
        var first = setup.AiOf(0);
        slots.Add(new ScriptedSkirmishSlot(match.Slots[0].Participant, first.Difficulty, IsHuman: humanPlays, Team: match.Slots[0].Description.Team, Kind: first.Kind));
        for (var index = 0; index < setup.Opponents.Length; index++)
        {
            var ai = setup.AiOf(index + 1);
            slots.Add(new ScriptedSkirmishSlot(match.Slots[index + 1].Participant, ai.Difficulty, Team: match.Slots[index + 1].Description.Team, Kind: ai.Kind));
        }
        var standard = ScriptedSkirmishOptions.Standard;
        var seed = match.Description.Seed;
        var options = standard with
        {
            Seed = seed, MaxTicks = setup.Options.MaxTicks, StartMoney = setup.Options.StartMoney,
            // Both are logic frames of the original in either set of options (SkirmishMatchOptions keeps the name "ticks" for its callers).
            GraceFrames = setup.Options.GraceTicks, SciencePointIntervalFrames = setup.Options.SciencePointInterval,
        };
        var session = ScriptedSkirmishSession.Start(options, data.Templates, data.Fingerprint, terrain, map, original.Library, original.Data, slots.ToImmutable());
        session.RecordCommands();
        return new LiveMatch(data, setup, match, session, terrain, map, seed, viewport);
    }
}

using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Net;
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

/// <summary>
/// Who plays a computer slot: the simulation's own AI (the default) or the original one with its script library; the army (easy, medium, hard) - for the built-in AI its
/// strength - and, for the built-in AI, its tactics profile (none: the AI as it was).
/// </summary>
public readonly record struct MatchAi(SkirmishAiKind Kind, ScriptDifficulty Difficulty = ScriptDifficulty.Normal, AiTactics? Tactics = null)
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

    /// <summary>The strength of the slot in the match description: 0 easy, 1 medium, 2 hard.</summary>
    public int Strength => Difficulty switch { ScriptDifficulty.Easy => 0, ScriptDifficulty.Hard => 2, _ => MatchSlotDescription.MediumStrength };

    public override string ToString() => Kind == SkirmishAiKind.Builtin
        ? "builtin" + (Tactics switch { AiTactics.Balanced => "/balanced", AiTactics.Rush => "/rush", AiTactics.Turtle => "/turtle", AiTactics.Eco => "/eco", _ => string.Empty }) + (Difficulty == ScriptDifficulty.Normal ? string.Empty : "/" + (Difficulty == ScriptDifficulty.Easy ? "easy" : "hard"))
        : "original/" + Difficulty switch { ScriptDifficulty.Easy => "easy", ScriptDifficulty.Hard => "hard", _ => "medium" };
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

    /// <summary>
    /// The slot of the viewer at this machine: whose orders the input gives, whose fog is shown, where the camera starts. 0 by default (the first slot, whose faction is
    /// <see cref="HumanFaction"/>). The match description does not say who is local; this does. A match with the original AI knows only slot 0.
    /// </summary>
    public int LocalSlot { get; init; }

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
            var human = slot == LocalSlot && !AutoPlay && !Spectator;
            var ai = AiOf(slot);
            // A human slot states the medium strength and no profile, whatever the list says: only a computer slot is played by an AI.
            slots.Add(new MatchSlotDescription(human ? MatchSlotKind.Human : MatchSlotKind.Computer, slot == 0 ? HumanFaction : Opponents[slot - 1], details.Team, details.Color, details.StartPosition,
                human ? MatchSlotDescription.MediumStrength : ai.Strength, AiTactics: !human && ai.Kind == SkirmishAiKind.Builtin && ai.Tactics is { } tactics ? (int)tactics : -1));
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
        : base(data, session.World, map, setup.MapPath, terrain, session.Players, LocalOf(setup, match).Player, fxSeed, viewport, HomeOf(setup, match, terrain))
    {
        Setup = setup;
        Match = match;
        _session = session;
    }

    private LiveMatch(GameData data, MatchSetup setup, ResolvedMatch match, ScriptedSkirmishSession session, TerrainMap terrain, MapData map, uint fxSeed, Vector2 viewport)
        : base(data, session.World, map, setup.MapPath, terrain, session.Players, LocalOf(setup, match).Player, fxSeed, viewport, HomeOf(setup, match, terrain))
    {
        Setup = setup;
        Match = match;
        Scripted = session;
        _scriptedPlayers = session.Players;
    }

    /// <summary>Where the local viewer starts to look: the start position of the first slot.</summary>
    private static ResolvedSlot LocalOf(MatchSetup setup, ResolvedMatch match)
        => match.Slots.FirstOrDefault(slot => slot.Slot == setup.LocalSlot) ?? throw new ArgumentException($"The viewer's slot {setup.LocalSlot} takes no part in the match.");

    private static Vector2 HomeOf(MatchSetup setup, ResolvedMatch match, TerrainMap terrain)
    {
        var start = terrain.StartPositions[LocalOf(setup, match).StartPosition];
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

    public override string HumanFaction => LocalOf(Setup, Match).Participant.Faction.Name;

    /// <summary>
    /// The embedded server this match runs through (docs/NETWORK-PROTOCOL.md section 7): the local skirmish takes its ticks and its commands from a relay like a network
    /// match, with one client and no delay.
    /// </summary>
    public CnCNew.Game.Lockstep.LocalRelay Relay => _relay ?? throw new InvalidOperationException("This match runs on a server somewhere else (Remote); it has no embedded one.");
    private CnCNew.Game.Lockstep.LocalRelay? _relay;

    /// <summary>The connection to the server of a match that was joined (<see cref="Join(GameData, CnCNew.Game.Lockstep.RemoteRelay, Vector2)"/>); null for a local game.</summary>
    public CnCNew.Game.Lockstep.RemoteRelay? Remote { get; private set; }

    /// <summary>A joined match: what this client remembers of its last ticks for the diagnosis of a desync, and where it writes its package (null: nowhere).</summary>
    public CnCNew.Game.Lockstep.SessionDiagnostics? Diagnostics { get; private set; }
    public string? DiagnosticsDirectory { get; set; }

    /// <summary>A joined match: all session hashes so far folded into one value, as a client without a window folds them - two clients in step agree in it.</summary>
    public ulong HashChain { get; private set; } = 0xCBF29CE484222325UL;

    /// <summary>A joined match: the desync the server reported, if it did.</summary>
    public CnCNew.Net.DesyncMessage? Desync => _client?.Desync;

    private CnCNew.Net.LockstepClient? _client;
    private uint _snapshotRequest;

    /// <summary>The lockstep driver of this match: its tick source and its command scheduler.</summary>
    public CnCNew.Game.Lockstep.LockstepDriver Lockstep { get; private set; } = null!;

    private bool _endReported;

    /// <summary>The match is built: tell the relay, and from now on take every tick and every command from it.</summary>
    private LiveMatch Join(CnCNew.Game.Lockstep.LocalRelay relay)
    {
        Func<ulong> hash = Scripted is { } scripted ? scripted.StateHash : Session.StateHash;
        relay.ReportLoaded(hash());
        _relay = relay;
        _client = relay.Client;
        Lockstep = new CnCNew.Game.Lockstep.LockstepDriver(relay.Client, 1.0 / World.Rate.TicksPerSecond, relay.Pump, hash, Match.Description.MaxTicks - 1);
        TickDriver = Lockstep;
        CommandPath.UseScheduler(Lockstep);
        return this;
    }

    /// <summary>
    /// The match is built from what a server sent: tell it, and take every tick and every command from it. Unlike the embedded server this one runs by its own clock,
    /// compares this client's hashes with the others', may ask this client for its state (somebody joins the running match) and may report a desync.
    /// </summary>
    /// <summary>Tells the server that this game has built the match (the hash of its tick 0); for a match built with <c>report: false</c>.</summary>
    public void ReportLoaded()
    {
        var remote = Remote ?? throw new InvalidOperationException("This match has no server.");
        remote.Client.ReportLoaded(Session.StateHash());
        remote.Pump();
    }

    private LiveMatch Join(CnCNew.Game.Lockstep.RemoteRelay remote, bool wait, bool report)
    {
        if (wait) remote.ReportLoaded(Session.StateHash());
        else if (report)
        {
            remote.Client.ReportLoaded(Session.StateHash());
            remote.Pump();
        }
        Remote = remote;
        _client = remote.Client;
        Diagnostics = new CnCNew.Game.Lockstep.SessionDiagnostics();
        Lockstep = new CnCNew.Game.Lockstep.LockstepDriver(remote.Client, 1.0 / World.Rate.TicksPerSecond, remote.Pump, Session.StateHash, Match.Description.MaxTicks - 1);
        TickDriver = Lockstep;
        CommandPath.UseScheduler(Lockstep);
        remote.Client.SnapshotRequested = request => _snapshotRequest = request;
        remote.Client.DesyncReceived = desync =>
        {
            remote.Client.SendDesyncDetail(desync.Tick, Diagnostics.TryGetParts(desync.Tick, out var parts) ? parts : []);
            if (DiagnosticsDirectory is { } directory) Diagnostics.WritePackage(directory, Session, remote.Client, desync);
        };
        return this;
    }

    /// <summary>What the frame produced - orders, hash reports - goes to the server now, not with the next frame.</summary>
    protected override void OnAdvanced(double gameSeconds) => Remote?.Flush();

    /// <summary>The lockstep probe of a match: the driver holds every order for <paramref name="ticks"/> ticks before it sends it to the relay.</summary>
    public override void UseInputDelay(int ticks)
    {
        if (Replay is null) Lockstep.InputDelayTicks = ticks;
    }

    public override bool IsOver => Replay is { } replay ? replay.Ends(Session) : Scripted?.IsOver ?? Session.IsOver;

    /// <summary>
    /// The playback this match shows (docs/REPLAY-BROWSER.md), or null for a match that is played. A playback has no server: its ticks come from the local tick driver
    /// (pause and speed are the presentation's), the commands of the humans from the recording, and nobody gives orders.
    /// </summary>
    public CnCNew.Game.Replays.ReplayPlayback? Replay { get; private init; }

    /// <summary>
    /// A presentation of <paramref name="session"/> as the playback of a replay: a spectator's match whose camera starts at the base of <paramref name="localPlayer"/> (the player
    /// the recording game played; 0 for none), with no relay behind it.
    /// </summary>
    internal static LiveMatch ForPlayback(GameData data, CnCNew.Game.Replays.ReplayPlayback playback, SkirmishMatchSession session, MapData map, TerrainMap terrain, Vector2 viewport, int localPlayer)
    {
        ArgumentNullException.ThrowIfNull(session);
        var seat = session.Match.Slots.FirstOrDefault(slot => slot.Player.Value == localPlayer) ?? session.Match.Slots[0];
        var setup = SetupOf(session.Description, session.Match, CnCNew.Game.Save.SkirmishViewer.Spectator) with { LocalSlot = seat.Slot };
        return new LiveMatch(data, setup, session.Match, session, terrain, map, session.Description.Seed, viewport) { Replay = playback };
    }
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

    protected override TickResult StepSimulation()
    {
        if (_snapshotRequest != 0 && Remote is { } asked && (World.Tick - asked.Client.StartTick) % asked.Client.Start!.HashInterval == 0)
        {
            // Between two ticks: the state after the last step, with nothing of the next bundle in it yet (docs/NETWORK-PROTOCOL.md section 18.2) - and where the
            // tick before it was reported, so that the relay knows the hash the state must have.
            var request = _snapshotRequest;
            _snapshotRequest = 0;
            asked.Client.OfferSnapshot(request, new CnCNew.Net.ResumeState(World.Tick, Session.StateHash(), Session.HumanCommandCounts, CnCNew.Game.Lockstep.SessionTransfer.Pack(Session.Save())));
        }
        var tick = World.Tick;
        if (Replay is { } replay)
        {
            replay.BeforeStep(Session);
            var played = Session.Step();
            replay.AfterStep(Session, played);
            return played;
        }
        var result = Scripted?.Step() ?? Session.Step();
        if (Remote is not null)
        {
            var hash = Session.StateHash();
            Diagnostics?.Record(tick, Session, hash);
            HashChain = unchecked((HashChain ^ hash) * 0x100000001B3UL);
        }
        if (IsOver && !_endReported)
        {
            _endReported = true;
            _client!.ReportEnd(World.Tick, Winner?.Value ?? 0);
        }
        return result;
    }

    public static LiveMatch Create(GameData data, MatchSetup setup, Vector2 viewport)
    {
        MapData map;
        using (var stream = data.Content.OpenRead(setup.MapPath)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, setup.MapPath);

        var humanPlays = !setup.AutoPlay && !setup.Spectator;
        if (setup.UsesOriginalAi && setup.LocalSlot != 0) throw new ArgumentException("A match with the original AI is viewed from its first slot.");
        // The room: the setup becomes the settings of a local server, the viewer takes his seat (or none), the server starts the match and hands back its description.
        var relay = CnCNew.Game.Lockstep.LocalRelay.Open(data.Fingerprint);
        relay.StartMatch(setup.Describe(data, terrain).ToRoomSettings(withSeed: true), humanPlays ? setup.LocalSlot : -1);
        var description = relay.Client.Description!;
        var match = description.Resolve(data.Templates, terrain, data.Fingerprint, CnCNew.Net.BuildIdentity.Current.Id);
        if (setup.UsesOriginalAi) return CreateScripted(data, setup, viewport, map, terrain, match, humanPlays).Join(relay);
        var session = SkirmishMatchSession.Start(description, data.Templates, terrain, map.Objects, setup.Options.CheckpointInterval, data.Fingerprint, CnCNew.Net.BuildIdentity.Current.Id);
        return new LiveMatch(data, setup, match, session, terrain, map, description.Seed, viewport).Join(relay);
    }

    /// <summary>
    /// Joins the match of a room on a server somewhere else (docs/NETWORK-PROTOCOL.md section 18.6). The room must have started its match
    /// (<see cref="CnCNew.Game.Lockstep.RemoteRelay.WaitForStart"/>): the description the server sent builds the same tick 0 as on every other member - or, for
    /// somebody who joins a running match, the state a member handed over is loaded. The viewer plays the seat the room gave this client; without a seat it watches.
    /// Throws <see cref="CnCNew.Game.Lockstep.RemoteRelayException"/> with a message fit to show when the match cannot be built here.
    /// </summary>
    public static LiveMatch Join(GameData data, CnCNew.Game.Lockstep.RemoteRelay remote, Vector2 viewport)
    {
        var client = remote.Client;
        var start = client.Start ?? throw new InvalidOperationException("The room has not started its match.");
        var description = client.Description!;
        if (description.TickRate != data.Rate.TicksPerSecond)
            throw new CnCNew.Game.Lockstep.RemoteRelayException($"The match runs at {description.TickRate} Hz; this game was started at {data.Rate.TicksPerSecond} Hz (--tick-rate {description.TickRate}).");
        var (map, terrain) = MapOf(data, description);
        return Build(data, remote, map, terrain, viewport, wait: true);
    }

    /// <summary>The first half of <see cref="Join(GameData, CnCNew.Game.Lockstep.RemoteRelay, Vector2)"/>: the map the description names, decoded, and its terrain.</summary>
    internal static (MapData Map, TerrainMap Terrain) MapOf(GameData data, MatchDescription description)
    {
        if (description.TickRate != data.Rate.TicksPerSecond)
            throw new CnCNew.Game.Lockstep.RemoteRelayException($"The match runs at {description.TickRate} Hz; this game was started at {data.Rate.TicksPerSecond} Hz (--tick-rate {description.TickRate}).");
        if (!data.Content.Exists(description.MapPath)) throw new CnCNew.Game.Lockstep.RemoteRelayException($"The match is played on '{description.MapPath}', which this game does not have.");
        MapData map;
        using (var stream = data.Content.OpenRead(description.MapPath)) map = SageMapDecoder.Decode(stream);
        return (map, TerrainMap.Build(map, description.MapPath));
    }

    /// <summary>
    /// The second half: the session is built from the description (or a member's state) and the server is told. With <paramref name="wait"/> the call returns when
    /// the server let the match begin; without it at once - the caller pumps the connection until the client is in the match (<see cref="NetMatchLoad"/>). Without
    /// <paramref name="report"/> the server is not told yet: the caller does what else it has to do before the first tick (the world's pictures) and then calls
    /// <see cref="ReportLoaded"/>, so that the server's clock does not start while this game is still busy.
    /// </summary>
    internal static LiveMatch Build(GameData data, CnCNew.Game.Lockstep.RemoteRelay remote, MapData map, TerrainMap terrain, Vector2 viewport, bool wait, bool report = true)
    {
        var client = remote.Client;
        var start = client.Start ?? throw new InvalidOperationException("The room has not started its match.");
        var description = client.Description!;
        SkirmishMatchSession session;
        try
        {
            session = start.Resume is { } resume
                ? SkirmishMatchSession.Restore(CnCNew.Game.Lockstep.SessionTransfer.Unpack(resume.State), data.Templates, data.Fingerprint, terrain)
                : SkirmishMatchSession.Start(description, data.Templates, terrain, map.Objects, 0, data.Fingerprint, client.Identity.Id);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or CnCNew.Simulation.Snapshots.SnapshotFormatException)
        {
            // A state that cannot be read is told to the server as one that does not match: it turns this client away and holds the seat again.
            if (start.Resume is not null) client.ReportLoaded(0);
            remote.Pump();
            throw new CnCNew.Game.Lockstep.RemoteRelayException($"The match cannot be built here: {exception.Message}");
        }
        var match = session.Match;
        var seat = match.Slots.FirstOrDefault(slot => slot.Player.Value == client.Player);
        var setup = SetupOf(description, match, seat is null ? CnCNew.Game.Save.SkirmishViewer.Spectator : CnCNew.Game.Save.SkirmishViewer.Player) with { LocalSlot = seat?.Slot ?? 0 };
        return new LiveMatch(data, setup, match, session, terrain, map, description.Seed, viewport).Join(remote, wait, report);
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
                var described = setup.Describe(data, terrain);
                var relay = Resume(data, described, setup, new CnCNew.Net.ResumeState(session.World.Tick, session.StateHash(), [], loaded.State));
                match = new LiveMatch(data, setup, described.Resolve(data.Templates, terrain), session, terrain, map, session.World.Configuration.Seed, viewport).Join(relay);
            }
            else
            {
                var session = SkirmishMatchSession.Restore(loaded.State, data.Templates, data.Fingerprint, terrain);
                var setup = SetupOf(session.Description, session.Match, loaded.Viewer);
                var relay = Resume(data, session.Description, setup, new CnCNew.Net.ResumeState(session.World.Tick, session.StateHash(), [..session.HumanCommandCounts], loaded.State));
                match = new LiveMatch(data, setup, session.Match, session, terrain, map, session.Description.Seed, viewport).Join(relay);
            }
            if (match.World.Tick != loaded.Header.Tick) throw new CnCNew.Game.Save.SaveGameException("The savegame's state does not match its header (the file is damaged).");
            return match;
        }
        catch (CnCNew.Simulation.Snapshots.SnapshotFormatException exception)
        {
            throw new CnCNew.Game.Save.SaveGameException($"The savegame cannot be restored: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The session of a playback at tick 0, built from the match description of the replay (docs/REPLAY-BROWSER.md). The game layer builds its sessions here and in the
    /// lockstep code only (the layer rule of the one match path); a playback plays a recording and is not played around a relay - it has no relay at all.
    /// </summary>
    internal static SkirmishMatchSession StartPlayback(GameData data, MatchDescription description, TerrainMap terrain, MapData map, int checkpointInterval, string simulationId)
        => SkirmishMatchSession.Start(description, data.Templates, terrain, map.Objects, checkpointInterval, data.Fingerprint, simulationId);

    /// <summary>A checkpoint of a playback restored: the session state the playback kept on its way (<see cref="CnCNew.Game.Replays.ReplayPlayback"/>).</summary>
    internal static SkirmishMatchSession RestorePlayback(GameData data, ReadOnlySpan<byte> state, TerrainMap terrain)
        => SkirmishMatchSession.Restore(state, data.Templates, data.Fingerprint, terrain);

    /// <summary>
    /// The room of a match that starts from a saved state: the settings of the saved description, the viewer's seat, and the state itself with its tick and its session hash
    /// (docs/NETWORK-PROTOCOL.md section 7). The server compares the hash every client reports after loading with the saved one.
    /// </summary>
    private static CnCNew.Game.Lockstep.LocalRelay Resume(GameData data, MatchDescription description, MatchSetup setup, CnCNew.Net.ResumeState resume)
    {
        var relay = CnCNew.Game.Lockstep.LocalRelay.Open(data.Fingerprint);
        relay.StartMatch(description.ToRoomSettings(withSeed: true), !setup.AutoPlay && !setup.Spectator ? setup.LocalSlot : -1, resume);
        return relay;
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

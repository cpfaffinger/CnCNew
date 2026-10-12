using CnCNew.Net;

namespace CnCNew.Server;

/// <param name="MaxRooms">How many rooms exist at a time; a hello for another one is rejected.</param>
/// <param name="HelloTimeoutMs">A connection that has not said its hello after this time is closed.</param>
/// <param name="Room">The options of every room (seeds come from the directory's source).</param>
public sealed record DirectoryOptions(int MaxRooms = 16, int HelloTimeoutMs = 5_000, RelayOptions? Room = null);

/// <summary>A room of the directory as a host sees it.</summary>
public readonly record struct RoomInfo(string Name, RelayServer Room);

/// <summary>
/// Several rooms behind one transport endpoint (docs/DEDICATED-SERVER.md): a connection belongs to the room its hello names. A room is created by the first hello that names
/// it and removed when its last connection is gone; each is a <see cref="RelayServer"/> of its own - lobby and match - and knows nothing of the others. Like a room, the
/// directory is single-threaded, pumped by its host, loads no game data and reads no clock but the injected one.
/// </summary>
public sealed class RoomDirectory
{
    private sealed class Pending(IConnection connection, long sinceMs, string origin)
    {
        public IConnection Connection { get; } = connection;
        public long SinceMs { get; } = sinceMs;
        public string Origin { get; } = origin;
    }

    /// <summary>A connection whose first message was already read: the room reads it again as its own first message.</summary>
    private sealed class Replayed(IConnection inner, byte[] first) : IConnection
    {
        private byte[]? _first = first;

        public bool IsOpen => inner.IsOpen;
        public void Send(ReadOnlySpan<byte> message) => inner.Send(message);
        public void SendUrgent(ReadOnlySpan<byte> message) => inner.SendUrgent(message);
        public void Close() => inner.Close();

        public bool TryReceive(out byte[] message)
        {
            if (_first is { } first)
            {
                _first = null;
                message = first;
                return true;
            }
            return inner.TryReceive(out message);
        }
    }

    private readonly INetClock _clock;
    private readonly DirectoryOptions _options;
    private readonly RelayOptions _roomOptions;
    private readonly List<Pending> _pending = [];
    private readonly SortedDictionary<string, RelayServer> _rooms = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inhabited = new(StringComparer.Ordinal);
    private readonly Queue<(string Room, RelayEvent Event)> _events = new();
    private readonly List<string> _empty = [];
    private readonly Queue<RoomInfo> _finished = new();
    private readonly HashSet<RelayServer> _reported = [];

    /// <param name="seedSource">Where the rooms take their seeds from: a cryptographic source of the host (a test names a sequence).</param>
    public RoomDirectory(INetClock clock, Func<uint> seedSource, DirectoryOptions? options = null)
    {
        _clock = clock;
        _options = options ?? new DirectoryOptions();
        if (_options.MaxRooms < 1) throw new ArgumentException("A server has at least one room.", nameof(options));
        _roomOptions = (_options.Room ?? new RelayOptions()) with { SeedSource = seedSource, HostMayChooseSeeds = false };
    }

    public int RoomCount => _rooms.Count;
    public int PendingConnections => _pending.Count;
    public IEnumerable<RoomInfo> Rooms => _rooms.Select(pair => new RoomInfo(pair.Key, pair.Value));
    public long RoomsOpened { get; private set; }
    public long RoomsClosed { get; private set; }

    /// <summary>Connections turned away before they reached a room: no room left, no hello in time, something else than a hello first.</summary>
    public long TurnedAway { get; private set; }

    /// <param name="origin">Where the connection comes from, as far as the rooms should know (an address without its port): what a kick remembers. Never logged here.</param>
    public void Attach(IConnection connection, string origin = "") => _pending.Add(new Pending(connection, _clock.Milliseconds, origin));

    public bool TryTakeEvent(out string room, out RelayEvent relayEvent)
    {
        if (_events.TryDequeue(out var entry))
        {
            (room, relayEvent) = entry;
            return true;
        }
        room = string.Empty;
        relayEvent = null!;
        return false;
    }

    /// <summary>
    /// A room whose match is over - ended, or abandoned by everybody: handed out once, for a host that keeps the recording (docs/DEDICATED-SERVER.md). The directory itself
    /// writes nothing.
    /// </summary>
    public bool TryTakeFinished(out RoomInfo room) => _finished.TryDequeue(out room);

    private void Log(string room, string name, params (string Key, string Value)[] details) => _events.Enqueue((room, new RelayEvent(name, details)));

    private static string Text(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Routes new connections, moves every room, and removes the rooms that are empty.</summary>
    public void Pump()
    {
        var now = _clock.Milliseconds;
        // In the order the connections came: whoever names a room first opens it and is its host.
        for (var index = 0; index < _pending.Count; index++)
        {
            var pending = _pending[index];
            if (!pending.Connection.IsOpen)
            {
                _pending.RemoveAt(index--);
                continue;
            }
            if (!pending.Connection.TryReceive(out var bytes))
            {
                if (now - pending.SinceMs < _options.HelloTimeoutMs) continue;
                TurnAway(index--, "no-hello", null);
                continue;
            }
            if (!MessageCodec.TryDecode(bytes, out var message, out _) || message is not HelloMessage hello)
            {
                TurnAway(index--, "not-a-hello", null);
                continue;
            }
            if (!_rooms.TryGetValue(hello.Room, out var room))
            {
                if (_rooms.Count >= _options.MaxRooms)
                {
                    TurnAway(index--, "server-full", new RejectMessage(RejectReason.ServerFull, $"This server holds {_options.MaxRooms} rooms and all are in use."));
                    continue;
                }
                _rooms[hello.Room] = room = new RelayServer(_clock, _roomOptions);
                RoomsOpened++;
                Log(hello.Room, "room-opened", ("rooms", Text(_rooms.Count)));
            }
            _pending.RemoveAt(index--);
            room.Attach(new Replayed(pending.Connection, bytes), pending.Origin);
        }
        foreach (var (name, room) in _rooms)
        {
            room.Pump();
            while (room.TryTakeEvent(out var relayEvent)) _events.Enqueue((name, relayEvent));
            if (room.Phase == RoomPhase.Ended && _reported.Add(room)) _finished.Enqueue(new RoomInfo(name, room));
            if (room.Members > 0) _inhabited.Add(name);
            // Empty: everybody left, or the one who opened it was rejected.
            if (room.Connections == 0) _empty.Add(name);
        }
        foreach (var name in _empty)
        {
            var room = _rooms[name];
            _rooms.Remove(name);
            _inhabited.Remove(name);
            // A match everybody walked away from was still a match.
            if (room.Recording.Bundles.Count > 0 && _reported.Add(room)) _finished.Enqueue(new RoomInfo(name, room));
            _reported.Remove(room);
            RoomsClosed++;
            Log(name, "room-closed", ("phase", room.Phase.ToString()), ("ticks", Text(room.NextTick)), ("lastAgreedTick", Text(room.LastAgreedTick)), ("intents", Text(room.IntentsRelayed)),
                ("discarded", Text(room.Discarded)), ("throttledMs", Text(room.ThrottledMicros / 1000)), ("droppedForLag", Text(room.DroppedForLag)), ("desync", room.Desync is null ? "false" : "true"),
                ("tickIntervals", room.TickIntervals.ToString()), ("recordingBytes", Text(room.RecordingBytes)), ("kicks", Text(room.Kicks)), ("badPasswords", Text(room.BadPasswords)),
                ("floodDrops", Text(room.FloodDrops)), ("hashStalls", Text(room.HashStallDrops)), ("loadTimeouts", Text(room.LoadTimeouts)), ("helloTimeouts", Text(room.HelloTimeouts)),
                ("statesRefused", Text(room.StatesRefusedForSize)), ("joinsRateLimited", Text(room.JoinsRateLimited)), ("rooms", Text(_rooms.Count)));
        }
        _empty.Clear();
    }

    private void TurnAway(int index, string why, RejectMessage? reject)
    {
        var pending = _pending[index];
        _pending.RemoveAt(index);
        TurnedAway++;
        if (reject is not null) pending.Connection.Send(MessageCodec.Encode(reject));
        pending.Connection.Close();
        Log(string.Empty, "turned-away", ("reason", why));
    }

    /// <summary>Microseconds of the clock until a room closes its next tick; -1 when no room runs a match by the clock. What a host loop may sleep at most.</summary>
    public long MicrosUntilNextTick
    {
        get
        {
            var least = -1L;
            foreach (var room in _rooms.Values)
            {
                var until = room.MicrosUntilNextTick;
                if (until >= 0 && (least < 0 || until < least)) least = until;
            }
            return least;
        }
    }
}

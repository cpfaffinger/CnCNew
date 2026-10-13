using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Server;

namespace CnCNew.Game.Lockstep;

/// <summary>What a game that hosts sets for its server; everything else is the default a dedicated server has (docs/DEDICATED-SERVER.md section 9.2).</summary>
/// <param name="Port">The UDP port; 0 takes a free one (<see cref="HostedServer.LocalEndPoint"/> says which).</param>
/// <param name="KeyFile">Where the host's permanent key is kept (made on the first start); null: a key that lasts as long as this server.</param>
public sealed record HostedServerOptions(int Port = 0, string Address = "0.0.0.0", string? KeyFile = null, int MaxRooms = 4, RelayOptions? Room = null, UdpOptions? Transport = null,
    bool ListRooms = true, int IdleWaitMicros = 2_000);

/// <summary>
/// "Host game" (docs/DEDICATED-SERVER.md section 14): the server of the dedicated program - the same <see cref="ServerCore"/>, its limits, its sealed transport, its
/// key - run inside a game for as long as the game wants. It has a thread of its own, as the dedicated server has a process of its own, so that its rooms close their
/// ticks by the clock whatever the hosting game's frames do; the hosting game talks to it through a socket like every other client. The network layers themselves
/// start no thread and read no clock - this class is the host that does both.
/// </summary>
public sealed class HostedServer : IDisposable
{
    private readonly UdpSocketChannel _socket;
    private readonly ServerCore _core;
    private readonly UdpIdentity _identity;
    private readonly Thread _thread;
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly int _idleWaitMicros;
    private volatile bool _running = true;
    private Exception? _failure;

    private HostedServer(HostedServerOptions options)
    {
        _identity = options.KeyFile is { Length: > 0 } file ? PlayerKeyFile.Load(file) : UdpIdentity.Create();
        var clock = new SystemNetClock();
        _socket = new UdpSocketChannel(new IPEndPoint(IPAddress.Parse(options.Address), options.Port));
        try
        {
            _core = new ServerCore(_socket, clock, RandomNumberGenerator.GetBytes(32), _identity, () => BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)), options.Transport,
                new DirectoryOptions(options.MaxRooms, Room: options.Room) { ListRooms = options.ListRooms });
        }
        catch
        {
            _socket.Dispose();
            _identity.Dispose();
            throw;
        }
        _idleWaitMicros = options.IdleWaitMicros;
        LocalEndPoint = _socket.LocalEndPoint;
        Fingerprint = _identity.Fingerprint;
        _thread = new Thread(Run) { IsBackground = true, Name = "CnCNew hosted server" };
        _thread.Start();
    }

    /// <summary>Starts the server; throws <see cref="RemoteRelayException"/> with a message fit to show when the port cannot be had or an option is unusable.</summary>
    public static HostedServer Start(HostedServerOptions? options = null)
    {
        try
        {
            return new HostedServer(options ?? new HostedServerOptions());
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or ArgumentException or FormatException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new RemoteRelayException($"This game cannot host: {exception.Message}");
        }
    }

    public IPEndPoint LocalEndPoint { get; }
    /// <summary>The fingerprint of the host's key: what the people who join are shown at their first meeting.</summary>
    public string Fingerprint { get; }
    public bool IsRunning => _running && _thread.IsAlive;
    /// <summary>What ended the server's thread, if anything did; a hosting game shows it instead of a silent room.</summary>
    public Exception? Failure => _failure;
    public long RoomsOpened { get; private set; }
    public long RoomsClosed { get; private set; }
    public int Connections { get; private set; }

    /// <summary>The server's events as the dedicated program would log them (<c>event=name room=... key=value</c>), oldest first; bounded.</summary>
    public bool TryTakeLine(out string line) => _lines.TryDequeue(out line!);

    private void Run()
    {
        using var timer = new FineTimer();
        try
        {
            while (_running)
            {
                _core.Pump();
                while (_core.TryTakeConnected(out _)) { }
                while (_core.Rooms.TryTakeEvent(out var room, out var relayEvent))
                {
                    if (_lines.Count >= 2_048) _lines.TryDequeue(out _);
                    _lines.Enqueue($"event={relayEvent.Name} room={room} " + string.Join(' ', relayEvent.Details.Select(detail => $"{detail.Key}={detail.Value}")));
                }
                // A hosted game keeps no recording of what it relayed: the hosting player's own client has the match.
                while (_core.Rooms.TryTakeFinished(out _)) { }
                while (_core.Endpoint.TryTakeClosed(out _, out _)) { }
                RoomsOpened = _core.Rooms.RoomsOpened;
                RoomsClosed = _core.Rooms.RoomsClosed;
                Connections = _core.Endpoint.Connections;
                var wait = _core.WaitMicros(_idleWaitMicros);
                if (wait > 0) _socket.WaitReadable(wait);
            }
            // Everybody who is connected is told that the connection ends, instead of finding out by a timeout.
            if (_core.CloseAll() > 0)
            {
                var clock = new SystemNetClock();
                var until = clock.Milliseconds + 300;
                while (clock.Milliseconds < until)
                {
                    _core.Endpoint.Pump();
                    _core.Endpoint.Flush();
                    _socket.WaitReadable(5_000);
                }
            }
        }
        catch (Exception exception)
        {
            // Nothing a client sends may end this thread (the layers below count and drop); if something does, the hosting game is told instead of going down with it.
            _failure = exception;
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive && !_thread.Join(3_000)) return;
        _core.Dispose();
        _socket.Dispose();
        _identity.Dispose();
    }
}

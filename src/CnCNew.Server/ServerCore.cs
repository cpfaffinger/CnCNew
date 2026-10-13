using System.Net;
using CnCNew.Net;
using CnCNew.Net.Udp;

namespace CnCNew.Server;

/// <summary>
/// What a server is between its socket and its rooms (docs/DEDICATED-SERVER.md section 14): the transport endpoint, the directory of rooms, and the one round of
/// work that moves both. The dedicated server program and a game that hosts a match for others run this same class; what differs is the host around it - who owns
/// the socket and the clock, where the key, the bans and the logs come from, and what waits between two rounds. Like everything in this layer it starts no thread,
/// opens no file, reads no clock but the injected one and draws no random number: the cookie secret and the seeds are handed in.
/// </summary>
public sealed class ServerCore : IDisposable
{
    private readonly Queue<IPEndPoint> _connected = new();

    /// <param name="cookieSecret">32 bytes only this server knows (the host draws them from a cryptographic source).</param>
    /// <param name="seedSource">Where the rooms take the seeds of their matches from (the host's cryptographic source; a test names a sequence).</param>
    public ServerCore(IDatagramChannel channel, INetClock clock, byte[] cookieSecret, UdpIdentity identity, Func<uint> seedSource, UdpOptions? transport = null, DirectoryOptions? directory = null,
        Func<IPAddress, bool>? isBanned = null)
    {
        Rooms = new RoomDirectory(clock, seedSource, directory);
        // The room's options are checked once here, so that a wrong value stops the server at its start and not at the first hello.
        _ = new RelayServer(clock, (directory?.Room ?? new RelayOptions()) with { SeedSource = () => 0 });
        Endpoint = new UdpServerEndpoint(channel, clock, cookieSecret, transport, identity, isBanned);
    }

    public UdpServerEndpoint Endpoint { get; }
    public RoomDirectory Rooms { get; }
    public long Rounds { get; private set; }

    /// <summary>
    /// One round: datagrams in, new connections to the directory, every room moved, datagrams out. What happened is read afterwards - <see cref="TryTakeConnected"/>,
    /// the directory's events and finished rooms, the endpoint's closed connections.
    /// </summary>
    public void Pump()
    {
        Rounds++;
        Endpoint.Pump();
        while (Endpoint.TryAccept(out var link, out var address))
        {
            // The room is told the address without its port - what a kick remembers.
            Rooms.Attach(link, address.Address.ToString());
            if (_connected.Count < 4_096) _connected.Enqueue(address);
        }
        Rooms.Pump();
        Endpoint.Flush();
    }

    /// <summary>A connection the last rounds accepted, for a host that logs it.</summary>
    public bool TryTakeConnected(out IPEndPoint address) => _connected.TryDequeue(out address!);

    /// <summary>The longest a host may sleep before the next round, in microseconds: until a room closes its next tick, at most <paramref name="idleMicros"/>.</summary>
    public int WaitMicros(int idleMicros)
    {
        var until = Rooms.MicrosUntilNextTick;
        return until < 0 ? idleMicros : (int)Math.Min(until, idleMicros);
    }

    /// <summary>Tells everybody who is connected that the connection ends, so no client waits for a timeout. The host pumps a few more rounds to deliver it.</summary>
    public int CloseAll() => Endpoint.CloseWhere(_ => true);

    public void Dispose() => Endpoint.Dispose();
}

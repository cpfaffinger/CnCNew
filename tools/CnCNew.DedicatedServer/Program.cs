using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Server;

// CnCNew.DedicatedServer (phase 26, docs/DEDICATED-SERVER.md): one process, one UDP port, several rooms. It relays - it loads no game data and simulates nothing.
return DedicatedServer.Run(args);

/// <summary>The configuration of the server: a JSON file, overridden by the command line. Every value has the documented default.</summary>
sealed record ServerConfig
{
    public string Address { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 27_960;
    public int MaxRooms { get; init; } = 16;
    /// <summary>Members a room admits: the players (at most 8 seats, by the protocol) and those who watch.</summary>
    public int MaxMembersPerRoom { get; init; } = Protocol.MaxConnections;
    public int DefaultTickRate { get; init; } = 60;
    public bool AllowModifiedBuilds { get; init; }
    public int HashInterval { get; init; } = 1;
    public int HelloTimeoutMs { get; init; } = 5_000;
    public int LagReportMs { get; init; } = 500;
    public int LagThrottleMs { get; init; } = 2_000;
    public int ThrottleLimitMs { get; init; } = 10_000;
    public int ThrottleSpeedPercent { get; init; } = 50;
    public int ConnectionTimeoutMs { get; init; } = 10_000;
    public int KeepAliveMs { get; init; } = 1_000;
    public int MaxConnections { get; init; } = 256;
    /// <summary>The longest the loop sleeps when nothing is due, in microseconds: how late a retransmission or a time limit may be noticed.</summary>
    public int IdleWaitMicros { get; init; } = 2_000;
    /// <summary>Ask Windows for a 1 ms timer while the server runs (docs/NETWORK-PROTOCOL.md section 13.4).</summary>
    public bool FineTimer { get; init; } = true;
    /// <summary>Every how many seconds a status line is written; 0: never.</summary>
    public int StatusSeconds { get; init; } = 30;
    /// <summary>Stop after this many rooms have closed (0: run until stopped); for a harness.</summary>
    public int ExitAfterRooms { get; init; }
    /// <summary>Stop after this many seconds (0: run until stopped); for a harness.</summary>
    public int RunSeconds { get; init; }
}

static class DedicatedServer
{
    private const string Usage = """
        Usage: CnCNew.DedicatedServer [--config <file.json>] [--port N] [--address A] [--max-rooms N] [--max-members N] [--tick-rate 30|60|120]
                                      [--allow-modified-builds] [--hash-interval N] [--lag-report-ms N] [--lag-throttle-ms N] [--throttle-limit-ms N]
                                      [--throttle-speed N] [--timeout-ms N] [--no-fine-timer] [--status-seconds N] [--exit-after-rooms N] [--run-seconds N]
          A relay for multiplayer matches: one UDP port, several rooms; a client names its room in its hello. Port 0 takes a free port (see the 'listening' line).
          Every line it writes is 'ts=<ms> event=<name> key=value ...'. Stop it with Ctrl+C.
        """;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    private static ServerConfig Configure(string[] args)
    {
        var config = new ServerConfig();
        var file = Array.IndexOf(args, "--config");
        if (file >= 0)
        {
            if (file + 1 >= args.Length) throw new ArgumentException("--config needs a file.");
            // An unknown key is an error, not a default.
            config = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(args[file + 1]), Json) ?? throw new ArgumentException("The configuration file is empty.");
        }
        for (var index = 0; index < args.Length; index++)
        {
            int Number() => index + 1 < args.Length ? int.Parse(args[++index], CultureInfo.InvariantCulture) : throw new ArgumentException($"{args[index]} needs a number.");
            config = args[index] switch
            {
                "--config" => Skip(ref index, config),
                "--port" => config with { Port = Number() },
                "--address" => config with { Address = index + 1 < args.Length ? args[++index] : throw new ArgumentException("--address needs an address.") },
                "--max-rooms" => config with { MaxRooms = Number() },
                "--max-members" => config with { MaxMembersPerRoom = Number() },
                "--tick-rate" => config with { DefaultTickRate = Number() },
                "--allow-modified-builds" => config with { AllowModifiedBuilds = true },
                "--hash-interval" => config with { HashInterval = Number() },
                "--lag-report-ms" => config with { LagReportMs = Number() },
                "--lag-throttle-ms" => config with { LagThrottleMs = Number() },
                "--throttle-limit-ms" => config with { ThrottleLimitMs = Number() },
                "--throttle-speed" => config with { ThrottleSpeedPercent = Number() },
                "--timeout-ms" => config with { ConnectionTimeoutMs = Number() },
                "--no-fine-timer" => config with { FineTimer = false },
                "--status-seconds" => config with { StatusSeconds = Number() },
                "--exit-after-rooms" => config with { ExitAfterRooms = Number() },
                "--run-seconds" => config with { RunSeconds = Number() },
                _ => throw new ArgumentException($"Unknown option {args[index]}.\n{Usage}"),
            };
        }
        if (config.Port is < 0 or > 65_535) throw new ArgumentException("The port is 0 to 65535.");
        if (config.HashInterval is < 1 or > ushort.MaxValue) throw new ArgumentException("The hash interval is 1 to 65535.");
        if (config.IdleWaitMicros < 100) throw new ArgumentException("The idle wait is at least 100 microseconds.");
        return config;
    }

    private static ServerConfig Skip(ref int index, ServerConfig config)
    {
        index++;
        return config;
    }

    private static string Value(string text)
        => text.Length > 0 && !text.AsSpan().ContainsAny(" \"=\t") ? text : "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    public static int Run(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Usage);
            return 0;
        }
        ServerConfig config;
        try
        {
            config = Configure(args);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        var clock = new SystemNetClock();
        var output = new StringBuilder();
        void Line(string name, string room, IEnumerable<(string Key, string Value)> details)
        {
            output.Clear();
            output.Append("ts=").Append(clock.Milliseconds.ToString(CultureInfo.InvariantCulture)).Append(" event=").Append(name);
            if (room.Length > 0 || name.StartsWith("room", StringComparison.Ordinal)) output.Append(" room=").Append(Value(room));
            foreach (var (key, value) in details) output.Append(' ').Append(key).Append('=').Append(Value(value));
            Console.WriteLine(output.ToString());
        }
        static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

        UdpSocketChannel socket;
        RoomDirectory directory;
        UdpServerEndpoint endpoint;
        try
        {
            socket = new UdpSocketChannel(new IPEndPoint(IPAddress.Parse(config.Address), config.Port));
            var relay = new RelayOptions(config.AllowModifiedBuilds, false, (ushort)config.HashInterval, null, config.MaxMembersPerRoom, config.LagReportMs, config.LagThrottleMs, config.ThrottleLimitMs,
                config.ThrottleSpeedPercent, config.DefaultTickRate);
            // Seeds and the cookie key come from the system's cryptographic source: nobody can predict a match's seed or forge a cookie.
            directory = new RoomDirectory(clock, () => BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)), new DirectoryOptions(config.MaxRooms, config.HelloTimeoutMs, relay));
            // The room is checked once here, so that a wrong value stops the server at its start and not at the first hello.
            _ = new RelayServer(clock, relay with { SeedSource = () => 0 });
            endpoint = new UdpServerEndpoint(socket, clock, RandomNumberGenerator.GetBytes(32),
                new UdpOptions { TimeoutMs = config.ConnectionTimeoutMs, KeepAliveMs = config.KeepAliveMs, MaxConnections = config.MaxConnections });
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or System.Net.Sockets.SocketException)
        {
            Console.Error.WriteLine($"The server cannot start: {exception.Message}");
            return 2;
        }

        using var timer = config.FineTimer ? new FineTimer() : null;
        var identity = BuildIdentity.Current;
        Line("listening", string.Empty,
        [
            ("address", socket.LocalEndPoint.Address.ToString()), ("port", Text(socket.LocalEndPoint.Port)), ("protocol", Text(Protocol.Version)), ("transport", Text(UdpFormat.Version)),
            ("build", identity.Id), ("maxRooms", Text(config.MaxRooms)), ("maxMembersPerRoom", Text(config.MaxMembersPerRoom)), ("allowModifiedBuilds", config.AllowModifiedBuilds ? "true" : "false"),
            ("lagReportMs", Text(config.LagReportMs)), ("lagThrottleMs", Text(config.LagThrottleMs)), ("throttleLimitMs", Text(config.ThrottleLimitMs)), ("throttleSpeedPercent", Text(config.ThrottleSpeedPercent)),
            ("fineTimer", timer?.Held == true ? "true" : "false"),
        ]);

        var running = true;
        Console.CancelKeyPress += (_, cancel) =>
        {
            cancel.Cancel = true;
            running = false;
        };
        var nextStatus = config.StatusSeconds > 0 ? config.StatusSeconds * 1000L : long.MaxValue;
        var loops = 0L;
        while (running)
        {
            loops++;
            endpoint.Pump();
            while (endpoint.TryAccept(out var link, out var address))
            {
                directory.Attach(link);
                Line("connected", string.Empty, [("from", address.ToString()), ("connections", Text(endpoint.Connections))]);
            }
            directory.Pump();
            while (directory.TryTakeEvent(out var room, out var relayEvent)) Line(relayEvent.Name, room, relayEvent.Details);
            endpoint.Flush();
            while (endpoint.TryTakeClosed(out var closed, out var from))
            {
                var counted = closed.Statistics;
                Line("disconnected", string.Empty,
                [
                    ("from", from.ToString()), ("reason", closed.CloseReason.ToString()), ("datagramsSent", Text(counted.DatagramsSent)), ("datagramsReceived", Text(counted.DatagramsReceived)),
                    ("bytesSent", Text(counted.BytesSent)), ("bytesReceived", Text(counted.BytesReceived)), ("segmentsSent", Text(counted.SegmentsSent)), ("segmentsRetransmitted", Text(counted.SegmentsRetransmitted)),
                    ("urgentSent", Text(counted.UrgentSent)), ("urgentRepeated", Text(counted.UrgentRepeated)), ("urgentOmitted", Text(counted.UrgentOmitted)), ("duplicatesReceived", Text(counted.DuplicatesReceived)),
                    ("malformed", Text(counted.Malformed)), ("roundTripMs", Text(counted.RoundTripMs)),
                ]);
            }

            var now = clock.Milliseconds;
            if (now >= nextStatus)
            {
                nextStatus = now + config.StatusSeconds * 1000L;
                Line("status", string.Empty,
                [
                    ("rooms", Text(directory.RoomCount)), ("connections", Text(endpoint.Connections)), ("pending", Text(directory.PendingConnections)), ("requests", Text(endpoint.Requests)),
                    ("cookiesRejected", Text(endpoint.CookiesRejected)), ("refused", Text(endpoint.Refused)), ("unknownPeer", Text(endpoint.UnknownPeer)), ("malformed", Text(endpoint.Malformed)),
                    ("turnedAway", Text(directory.TurnedAway)), ("sendFailures", Text(socket.SendFailures)), ("managedKb", Text(GC.GetTotalMemory(false) / 1024)), ("gen2", Text(GC.CollectionCount(2))),
                    ("loops", Text(loops)),
                ]);
                foreach (var info in directory.Rooms)
                    Line("room-status", info.Name,
                    [
                        ("phase", info.Room.Phase.ToString()), ("members", Text(info.Room.Members)), ("tick", Text(info.Room.NextTick)), ("lastAgreedTick", Text(info.Room.LastAgreedTick)),
                        ("speedPercent", Text(info.Room.EffectiveSpeedPercent)), ("throttledMs", Text(info.Room.ThrottledMicros / 1000)), ("tickIntervals", info.Room.TickIntervals.ToString()),
                    ]);
            }
            if (config.ExitAfterRooms > 0 && directory.RoomsClosed >= config.ExitAfterRooms && directory.RoomCount == 0) break;
            if (config.RunSeconds > 0 && now >= config.RunSeconds * 1000L) break;

            // Wait on the socket: a datagram wakes the loop at once; otherwise it sleeps until the next tick of a room is due, at most the idle wait.
            var until = directory.MicrosUntilNextTick;
            var wait = until < 0 ? config.IdleWaitMicros : (int)Math.Min(until, config.IdleWaitMicros);
            if (wait > 0) socket.WaitReadable(wait);
        }
        Line("stopped", string.Empty, [("roomsOpened", Text(directory.RoomsOpened)), ("roomsClosed", Text(directory.RoomsClosed)), ("loops", Text(loops))]);
        socket.Dispose();
        return 0;
    }
}

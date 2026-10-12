using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CnCNew.Game.Data;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Testing;

// Phase 26 (docs/NETWORK-PROTOCOL.md section 15): real sockets and real processes - the UDP proxy, the client without a window, and the harness that runs a match of them.
static partial class NetTool
{
    private static double Real(string[] args, string name, double fallback) => Option(args, name) is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : fallback;

    private static IPEndPoint EndPoint(string text)
    {
        var colon = text.LastIndexOf(':');
        if (colon <= 0) throw new ArgumentException($"'{text}' is not host:port.");
        var host = text[..colon];
        var address = host is "localhost" ? IPAddress.Loopback : IPAddress.Parse(host);
        return new IPEndPoint(address, int.Parse(text[(colon + 1)..], CultureInfo.InvariantCulture));
    }

    private static Impairment ImpairmentOf(string[] args)
        => new((int)Number(args, "--delay", 0), (int)Number(args, "--jitter", 0), (int)Math.Round(Real(args, "--loss-percent", 0) * 10), (int)Math.Round(Real(args, "--dup-percent", 0) * 10),
            (int)Math.Round(Real(args, "--reorder-percent", 0) * 10), RateKbps: (int)Number(args, "--rate-kbps", 0), QueueBytes: (int)Number(args, "--queue-kb", 64) * 1000);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------------------------ proxy

    private sealed class ProxySession(UdpSocketChannel upstream, IPEndPoint client, DatagramImpairer<int> up, DatagramImpairer<int> down)
    {
        public UdpSocketChannel Upstream { get; } = upstream;
        public IPEndPoint Client { get; } = client;
        public DatagramImpairer<int> Up { get; } = up;
        public DatagramImpairer<int> Down { get; } = down;
    }

    /// <summary>
    /// The network simulator for real sockets: clients send to the proxy as if it were the server; for each of them the proxy opens a socket of its own toward the server, so
    /// the server sees one address per client. Both directions of every client pass a <see cref="DatagramImpairer{T}"/> - the same disturbance the tests use in memory.
    /// </summary>
    public static int Proxy(string[] args)
    {
        var server = EndPoint(Option(args, "--server") ?? throw new ArgumentException(Usage));
        var impairment = ImpairmentOf(args);
        var seed = (ulong)Number(args, "--seed", 1);
        var runSeconds = Number(args, "--run-seconds", 0);
        var clock = new SystemNetClock();
        using var timer = new FineTimer();
        using var listen = new UdpSocketChannel(new IPEndPoint(IPAddress.Loopback.AddressFamily == server.AddressFamily ? IPAddress.Any : IPAddress.IPv6Any, (int)Number(args, "--listen", 0)));
        var sessions = new Dictionary<IPEndPoint, ProxySession>();
        var buffer = new byte[2048];
        var running = true;
        Console.CancelKeyPress += (_, cancel) =>
        {
            cancel.Cancel = true;
            running = false;
        };
        Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=listening port={listen.LocalEndPoint.Port} server={server} delayMs={impairment.DelayMs} jitterMs={impairment.JitterMs} lossPerMille={impairment.LossPerMille} duplicatePerMille={impairment.DuplicatePerMille} reorderPerMille={impairment.ReorderPerMille} seed={seed}"));
        while (running && (runSeconds <= 0 || clock.Milliseconds < runSeconds * 1000))
        {
            var busy = false;
            while (listen.TryReceive(buffer, out var length, out var from))
            {
                if (!sessions.TryGetValue(from, out var session))
                {
                    var index = (ulong)sessions.Count;
                    session = new ProxySession(new UdpSocketChannel(new IPEndPoint(server.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0)), from,
                        new DatagramImpairer<int>(clock, impairment, seed * 1_000_003UL + index * 2 + 1), new DatagramImpairer<int>(clock, impairment, seed * 1_000_003UL + index * 2 + 2));
                    sessions[from] = session;
                    Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=client from={from} via={session.Upstream.LocalEndPoint.Port}"));
                }
                session.Up.Offer(buffer.AsSpan(0, length), 0);
                busy = true;
            }
            foreach (var session in sessions.Values)
            {
                while (session.Upstream.TryReceive(buffer, out var length, out _))
                {
                    session.Down.Offer(buffer.AsSpan(0, length), 0);
                    busy = true;
                }
                while (session.Up.TryTake(out var datagram, out _)) session.Upstream.Send(datagram, server);
                while (session.Down.TryTake(out var datagram, out _)) listen.Send(datagram, session.Client);
                busy |= session.Up.Queued > 0 || session.Down.Queued > 0;
            }
            // While datagrams are on their way the loop spins, so that a delay of 10 ms is 10 ms and not the timer's next interrupt; idle, it sleeps on the socket.
            if (busy) Thread.SpinWait(200);
            else listen.WaitReadable(1_000);
        }
        long offered = 0, lost = 0, duplicated = 0, reordered = 0, overflowed = 0;
        foreach (var session in sessions.Values)
        {
            offered += session.Up.Offered + session.Down.Offered;
            lost += session.Up.Lost + session.Down.Lost;
            duplicated += session.Up.Duplicated + session.Down.Duplicated;
            reordered += session.Up.Reordered + session.Down.Reordered;
            overflowed += session.Up.Overflowed + session.Down.Overflowed;
            session.Upstream.Dispose();
        }
        Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=stopped clients={sessions.Count} datagrams={offered} lost={lost} duplicated={duplicated} reordered={reordered} overflowed={overflowed}"));
        return 0;
    }

    // ----------------------------------------------------------------------------------------------------------- client

    private sealed record Spread(int Count, double Mean, double Min, double Median, double P95, double P99, double Max)
    {
        public static Spread Of(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(value => value).ToArray();
            if (sorted.Length == 0) return new Spread(0, 0, 0, 0, 0, 0, 0);
            double At(double share) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * share))];
            return new Spread(sorted.Length, sorted.Average(), sorted[0], At(0.5), At(0.95), At(0.99), sorted[^1]);
        }
    }

    /// <summary>What a client process measured, written as JSON for the harness.</summary>
    private sealed record ClientResult
    {
        public string Name { get; init; } = string.Empty;
        public int Seat { get; init; }
        public int Player { get; init; }
        public bool Ok { get; init; }
        public string Problem { get; init; } = string.Empty;
        public string Phase { get; init; } = string.Empty;
        public string Disturbance { get; init; } = string.Empty;
        public long TargetTick { get; init; }
        public long TicksPlayed { get; init; }
        public string HashChain { get; init; } = string.Empty;
        public string FinalSessionHash { get; init; } = string.Empty;
        public long IntentsSent { get; init; }
        public Spread LatencyMs { get; init; } = Spread.Of([]);
        public Spread LatencyTicks { get; init; } = Spread.Of([]);
        public Spread BufferMs { get; init; } = Spread.Of([]);
        public Spread PlaybackDelayMs { get; init; } = Spread.Of([]);
        public long Stalls { get; init; }
        public long Resyncs { get; init; }
        public int MaxRateAdjustPerMille { get; init; }
        public double RoundTripMs { get; init; }
        public double MatchSeconds { get; init; }
        public double BytesSentPerSecond { get; init; }
        public double BytesReceivedPerSecond { get; init; }
        public long DatagramsSent { get; init; }
        public long DatagramsReceived { get; init; }
        public long SegmentsRetransmitted { get; init; }
        public long DuplicatesReceived { get; init; }
        public long Malformed { get; init; }
        public long Discarded { get; init; }
        public string LastDiscard { get; init; } = string.Empty;
        public int MaxLagSeenMs { get; init; }
        public int MinSpeedSeenPercent { get; init; } = 100;
        public string[] Drops { get; init; } = [];
        public double MaxFrameMs { get; init; }
        public int MaxTicksInFrame { get; init; }
        public int Gen0 { get; init; }
        public int Gen1 { get; init; }
        public int Gen2 { get; init; }
        public double ManagedMbAtStart { get; init; }
        public double ManagedMbAtEnd { get; init; }
        public double WorkingSetMbAtStart { get; init; }
        public double WorkingSetMbAtEnd { get; init; }
        public string Build { get; init; } = string.Empty;
        /// <summary>The operating system and runtime the process ran on: what a match across the boundary of two systems is checked by.</summary>
        public string Os { get; init; } = string.Empty;
        public string Runtime { get; init; } = string.Empty;
        /// <summary>The tick this client's session began at: 0, or the tick of the state it joined the running match with.</summary>
        public long JoinedAtTick { get; init; }
        /// <summary>A rejoin: the tick the connection was dropped at, the bytes of the state as it came over the wire, milliseconds from the new connection to the state
        /// being loaded and to the client playing at the head of the match again.</summary>
        public long RejoinGoneAtTick { get; init; } = -1;
        public int RejoinStateBytes { get; init; }
        public double RejoinLoadedMs { get; init; }
        public double RejoinCaughtUpMs { get; init; }
        /// <summary>States this client handed over to a joiner, and what the last one cost.</summary>
        public int SnapshotsOffered { get; init; }
        public int SnapshotStateBytes { get; init; }
        public int SnapshotPackedBytes { get; init; }
        public double SnapshotMs { get; init; }
        public long DesyncTick { get; init; } = -1;
        public string DesyncParts { get; init; } = string.Empty;
        public string Corrupted { get; init; } = string.Empty;
        public string Package { get; init; } = string.Empty;
    }

    private static readonly JsonSerializerOptions ResultJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Client(string[] args)
    {
        var mapPath = Option(args, "--map");
        var serverText = Option(args, "--server");
        if (args.Length < 2 || mapPath is null || serverText is null) throw new ArgumentException(Usage);
        var server = EndPoint(serverText);
        var rate = Rate(args);
        var name = Option(args, "--name") ?? "client";
        var seat = (int)Number(args, "--seat", 0);
        var humans = (int)Number(args, "--humans", 2);
        var ais = (int)Number(args, "--ais", 0);
        var members = (int)Number(args, "--members", humans);
        var room = Option(args, "--room") ?? string.Empty;
        var resultFile = Option(args, "--result");
        var frameMicros = (int)Number(args, "--frame-us", 4_000);
        var maxTicksPerFrame = (int)Number(args, "--max-ticks-per-frame", 8);
        var leaveAt = Number(args, "--leave-at-tick", -1);
        var vanishAt = Number(args, "--vanish-at-tick", -1);
        var freezeAt = Number(args, "--freeze-at-tick", -1);
        var slowMs = (int)Number(args, "--slow-ms", 0);
        var slowFrom = Number(args, "--slow-from-tick", 0);
        var hitchAt = Number(args, "--hitch-at-tick", -1);
        var hitchMs = (int)Number(args, "--hitch-ms", 500);
        var watch = args.Contains("--watch");
        var rejoinAt = Number(args, "--rejoin-at-tick", -1);
        var rejoinAfterMs = (int)Number(args, "--rejoin-after-ms", 2_000);
        var corruptAt = Number(args, "--corrupt-at-tick", -1);
        var diagnostics = Option(args, "--diagnostics");
        var disturbance = rejoinAt >= 0 ? $"rejoin@{rejoinAt}" : corruptAt >= 0 ? $"corrupt@{corruptAt}" : leaveAt >= 0 ? $"leave@{leaveAt}" : vanishAt >= 0 ? $"vanish@{vanishAt}" : freezeAt >= 0 ? $"freeze@{freezeAt}" : slowMs > 0 ? $"slow {slowMs} ms from {slowFrom}" : hitchAt >= 0 ? $"hitch {hitchMs} ms @{hitchAt}" : string.Empty;

        var data = GameData.Load(args[1], modelGeometry: true, rate: rate);
        var loaded = Load(data, mapPath);
        var target = Ticks(args, rate, 120);
        var settings = Settings(loaded, humans, ais, Math.Max(target, SkirmishMatchOptions.StandardAt(rate).MaxTicks));
        var timeoutSeconds = Number(args, "--timeout-seconds", target / rate.TicksPerSecond * 4 + 300);
        var maps = new Dictionary<string, MatchMap>(StringComparer.Ordinal) { [mapPath] = new MatchMap(loaded.Terrain, loaded.Map.Objects) };
        MatchMap LoadMap(string path)
        {
            if (!maps.TryGetValue(path, out var map))
            {
                var other = Load(data, path);
                maps[path] = map = new MatchMap(other.Terrain, other.Map.Objects);
            }
            return map;
        }

        // The code of a tick is compiled before the match, not during its first second: a throw-away match of this map, played for a few game seconds.
        if (!args.Contains("--cold"))
        {
            var warm = SkirmishMatchSession.Start(settings.ToDescription(string.Empty, data.Fingerprint.ToUpperInvariant(), 1, 2), data.Templates, loaded.Terrain, loaded.Map.Objects);
            for (var tick = 0; tick < 10 * rate.TicksPerSecond; tick++) warm.Step();
        }
        // What loading and warming left behind is collected now, not during the match.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var clock = new SystemNetClock();
        using var timer = new FineTimer();
        UdpSocketChannel socket = null!;
        UdpClientEndpoint endpoint = null!;
        // A connection of its own and a client on it; with a token the client asks for the seat it had (docs/NETWORK-PROTOCOL.md section 18.2).
        HeadlessClient Connect(ulong token)
        {
            socket = new UdpSocketChannel(new IPEndPoint(server.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
            // --server-key: the key the server must show (the harness reads it from the server's first line); any other ends the attempt.
            var pinned = (Option(args, "--server-key") ?? string.Empty).ToUpperInvariant();
            endpoint = new UdpClientEndpoint(socket, server, clock, BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)),
                trust: pinned.Length == 0 ? null : key => UdpCrypto.FingerprintOf(key) == pinned);
            return new HeadlessClient(endpoint.Connection, name, data.Templates, data.Fingerprint, LoadMap, room, clock, generatorSalt: (uint)Number(args, "--salt", 0), rejoinToken: token, watch: watch,
                roomPassword: Option(args, "--room-password") ?? string.Empty)
            {
                KeepHashes = false, CorruptAtTick = corruptAt, DiagnosticsDirectory = diagnostics,
            };
        }
        var headless = Connect(0);
        var client = headless.Client;
        Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=started name={name} seat={seat} server={server} room=\"{room}\" build={client.Identity.Id} targetTick={target}"));

        bool configured = false, seated = false, endReported = false, left = false, frozen = false, hitched = false, rejoined = false, loadedSeen = false, caughtUp = false;
        long goneAtTick = -1, backAtMicros = -1, desyncAtMicros = -1;
        double loadedMs = 0, caughtUpMs = 0;
        var stateBytes = 0;
        var ticksBefore = 0L;
        var offered = (Count: 0, StateBytes: 0, PackedBytes: 0, Micros: 0L);
        long lastStartRequest = long.MinValue, matchStartMicros = -1, matchEndMicros = -1, bytesSentAtStart = 0, bytesReceivedAtStart = 0, endReportedAt = 0;
        var problem = string.Empty;
        var bufferSamples = new List<double>();
        var delaySamples = new List<double>();
        var maxAdjust = 0;
        var maxLag = 0;
        var minSpeed = 100;
        var maxFrame = 0.0;
        var maxTicksInFrame = 0;
        int gen0 = 0, gen1 = 0, gen2 = 0;
        double managedStart = 0, workingStart = 0;
        var lastPhase = client.Phase;
        var frozenAtMicros = -1L;
        var lastLoop = clock.Microseconds;
        var statusAt = 10_000L;

        while (true)
        {
            var now = clock.Microseconds;
            if (matchStartMicros >= 0 && !frozen) maxFrame = Math.Max(maxFrame, (now - lastLoop) / 1000.0);
            lastLoop = now;
            if (!frozen)
            {
                endpoint.Pump();
                var played = headless.Update(frameMicros / 1_000_000.0, target, maxTicksPerFrame);
                maxTicksInFrame = Math.Max(maxTicksInFrame, played);

                if (client.Phase != lastPhase)
                {
                    Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=phase name={name} phase={client.Phase} connection={client.ConnectionId} host={(client.IsHost ? "true" : "false")} player={client.Player}"));
                    lastPhase = client.Phase;
                }
                if (client.Phase == ClientPhase.InRoom)
                {
                    // Whoever is the host sets the room; everybody takes the seat the harness gave it; the host starts when all members are there and seated.
                    if (client.IsHost && !configured)
                    {
                        client.Configure(settings);
                        configured = true;
                    }
                    if (!seated && client.Room is { } state && state.Settings.Slots.Length == settings.Slots.Length)
                    {
                        if (seat >= 0)
                        {
                            var slot = settings.Slots[seat];
                            client.RequestSlot(seat, slot.Faction, slot.Team, slot.Color, slot.StartPosition);
                            client.SetReady(true);
                        }
                        seated = true;
                    }
                    if (client.IsHost && seated && client.Room is { } full && full.Members.Length == members && full.Members.Count(member => member.Slot >= 0) == humans
                        && full.Members.All(member => member.Slot < 0 || member.Ready || member.IsHost) && (lastStartRequest == long.MinValue || now - lastStartRequest > 500_000))
                    {
                        lastStartRequest = now;
                        client.RequestStart();
                    }
                }
                if (client.Phase == ClientPhase.InMatch && matchStartMicros < 0)
                {
                    matchStartMicros = now;
                    bytesSentAtStart = endpoint.Link.Statistics.BytesSent;
                    bytesReceivedAtStart = endpoint.Link.Statistics.BytesReceived;
                    gen0 = GC.CollectionCount(0);
                    gen1 = GC.CollectionCount(1);
                    gen2 = GC.CollectionCount(2);
                    managedStart = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                    using var process = Process.GetCurrentProcess();
                    workingStart = process.WorkingSet64 / (1024.0 * 1024.0);
                }
                if (matchStartMicros >= 0 && headless.Driver is { } driver)
                {
                    if (driver.Playback.Synced && headless.Tick < target)
                    {
                        bufferSamples.Add(driver.Playback.BufferMs(client.NewestBundleTick));
                        delaySamples.Add(driver.Playback.DelayMicros / 1000.0);
                        maxAdjust = Math.Max(maxAdjust, Math.Abs(driver.Playback.RateAdjustPerMille));
                    }
                    // A client that cannot keep up: every tick costs it this much more.
                    if (slowMs > 0 && played > 0 && headless.Tick >= slowFrom) Thread.Sleep(slowMs * played);
                }
                foreach (var lag in client.Lag.Values) maxLag = Math.Max(maxLag, lag.LagMs);
                if (client.Phase == ClientPhase.InMatch) minSpeed = Math.Min(minSpeed, client.Paused ? 0 : client.SpeedPercent);

                if (headless.SnapshotsOffered > offered.Count)
                {
                    offered = (headless.SnapshotsOffered, headless.LastSnapshot.StateBytes, headless.LastSnapshot.PackedBytes, headless.LastSnapshot.Micros);
                    Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=snapshot-offered name={name} tick={headless.Tick} stateBytes={offered.StateBytes} packedBytes={offered.PackedBytes} ms={offered.Micros / 1000.0:F1}"));
                }
                if (rejoined && backAtMicros >= 0)
                {
                    if (!loadedSeen && headless.Session is not null)
                    {
                        loadedSeen = true;
                        loadedMs = (now - backAtMicros) / 1000.0;
                        stateBytes = client.Start?.Resume?.State.Length ?? 0;
                        Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=rejoin-loaded name={name} tick={headless.JoinedAtTick} wireBytes={stateBytes} ms={loadedMs:F0}"));
                    }
                    // At the head of the match again: the playback position is reached, nothing is left to catch up.
                    if (loadedSeen && !caughtUp && headless.Driver is { Playback.Synced: true, BehindTicks: <= 1 })
                    {
                        caughtUp = true;
                        caughtUpMs = (now - backAtMicros) / 1000.0;
                        Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=rejoin-caught-up name={name} tick={headless.Tick} ms={caughtUpMs:F0}"));
                    }
                }
                // The connection is gone without a word, and after a while the same player is back with its token.
                if (rejoinAt >= 0 && !rejoined && headless.Tick >= rejoinAt && client.Phase == ClientPhase.InMatch)
                {
                    rejoined = true;
                    goneAtTick = headless.Tick;
                    ticksBefore = headless.TicksPlayed;
                    var token = client.RejoinToken;
                    Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=connection-dropped name={name} tick={goneAtTick} token={(token != 0 ? "yes" : "none")}"));
                    socket.Dispose();
                    Thread.Sleep(rejoinAfterMs);
                    headless = Connect(token);
                    client = headless.Client;
                    lastPhase = client.Phase;
                    backAtMicros = clock.Microseconds;
                    lastLoop = backAtMicros;
                    matchStartMicros = -1;
                    continue;
                }
                // What the harness asked this client to do to the match.
                if (hitchAt >= 0 && !hitched && headless.Tick >= hitchAt)
                {
                    hitched = true;
                    Thread.Sleep(hitchMs);
                }
                if (leaveAt >= 0 && !left && headless.Tick >= leaveAt)
                {
                    left = true;
                    client.Leave();
                    Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=leaving name={name} tick={headless.Tick}"));
                }
                if (vanishAt >= 0 && headless.Tick >= vanishAt)
                {
                    // The process is gone without a word: no close, no goodbye.
                    Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=vanishing name={name} tick={headless.Tick}"));
                    problem = "vanished as asked";
                    break;
                }
                if (freezeAt >= 0 && headless.Tick >= freezeAt)
                {
                    frozen = true;
                    frozenAtMicros = now;
                    Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=freezing name={name} tick={headless.Tick}"));
                }
                if (headless.Tick >= target && !endReported && client.Phase == ClientPhase.InMatch)
                {
                    endReported = true;
                    endReportedAt = now;
                    matchEndMicros = now;
                    client.ReportEnd(target, 0);
                }
                endpoint.Flush();
            }

            if (headless.Problem.Length > 0) { problem = headless.Problem; break; }
            if (client.Phase == ClientPhase.Rejected) { problem = $"rejected ({client.Rejection!.Reason}): {client.Rejection.Text}"; break; }
            if (endpoint.State == UdpClientState.Failed)
            {
                problem = endpoint.Link.CloseReason == UdpCloseReason.Untrusted ? $"the server shows the key {endpoint.ServerFingerprint}, not the one this client was told to expect" : "no connection to the server";
                break;
            }
            // After a desync the relay still names the parts the clients differ in: this client waits a few seconds for that.
            if (client.Desync is not null && desyncAtMicros < 0) desyncAtMicros = now;
            if (client.Phase == ClientPhase.Ended && (client.Desync is null || client.DesyncReport is not null || now - desyncAtMicros > 5_000_000)) break;
            if (client.Phase == ClientPhase.Ended) { socket.WaitReadable(frameMicros); continue; }
            if (!frozen && endpoint.State == UdpClientState.Closed) { if (!left && !endReported) problem = $"the connection was closed ({endpoint.Link.CloseReason}) at tick {headless.Tick}"; break; }
            // The others have not all reported their end (one was dropped, or is behind): this client has played what it was asked to.
            if (endReported && now - endReportedAt > 20_000_000) break;
            if (now > timeoutSeconds * 1_000_000) { problem = $"timeout in phase {client.Phase} at tick {headless.Tick}"; break; }
            if (frozen && frozenAtMicros >= 0 && now - frozenAtMicros > 30_000_000) break;
            if (clock.Milliseconds >= statusAt)
            {
                statusAt += 10_000;
                Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=status name={name} phase={client.Phase} tick={headless.Tick} newestBundle={client.NewestBundleTick} rttMs={endpoint.Link.Statistics.RoundTripMs} delayMs={(headless.Driver?.Playback.DelayMicros ?? 0) / 1000.0:F1} stalls={headless.Driver?.Stalls ?? 0}"));
            }
            if (frozen) Thread.Sleep(50);
            else socket.WaitReadable(frameMicros);
        }

        if (matchEndMicros < 0) matchEndMicros = clock.Microseconds;
        // A goodbye, unless the process is to vanish: the close is delivered before the socket goes.
        if (vanishAt < 0 && !frozen)
        {
            endpoint.Connection.Close();
            for (var round = 0; round < 1_500 && !endpoint.Link.IsFinished; round++)
            {
                endpoint.Pump();
                endpoint.Flush();
                socket.WaitReadable(2_000);
            }
        }
        var seconds = matchStartMicros < 0 ? 0 : (matchEndMicros - matchStartMicros) / 1_000_000.0;
        var statistics = endpoint.Link.Statistics;
        using var self = Process.GetCurrentProcess();
        var disturbed = disturbance.Length > 0 && !disturbance.StartsWith("hitch", StringComparison.Ordinal) && !disturbance.StartsWith("rejoin", StringComparison.Ordinal);
        if (client.Desync is not null && problem.Length == 0) problem = Invariant($"the server reported a desync at tick {client.Desync.Tick}");
        var result = new ClientResult
        {
            Name = name, Seat = seat, Player = client.Player, Problem = problem, Phase = client.Phase.ToString(), Disturbance = disturbance, TargetTick = target, TicksPlayed = ticksBefore + headless.TicksPlayed,
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            JoinedAtTick = headless.JoinedAtTick, RejoinGoneAtTick = goneAtTick, RejoinStateBytes = stateBytes, RejoinLoadedMs = loadedMs, RejoinCaughtUpMs = caughtUpMs,
            SnapshotsOffered = offered.Count, SnapshotStateBytes = offered.StateBytes, SnapshotPackedBytes = offered.PackedBytes, SnapshotMs = offered.Micros / 1000.0,
            DesyncTick = client.Desync?.Tick ?? -1, DesyncParts = client.DesyncReport is { } report ? string.Join("; ", report.Parts.Select(part => part.Name)) : string.Empty,
            Corrupted = headless.Corrupted, Package = headless.PackageWritten ?? string.Empty,
            Ok = problem.Length == 0 && headless.Tick >= target && !disturbed,
            HashChain = headless.HashChain.ToString("X16", CultureInfo.InvariantCulture), FinalSessionHash = (headless.Session?.StateHash() ?? 0).ToString("X16", CultureInfo.InvariantCulture),
            IntentsSent = client.IntentsSent, LatencyMs = Spread.Of(headless.LatencyMicros.Select(value => value / 1000.0)), LatencyTicks = Spread.Of(headless.LatencyTicks.Select(value => (double)value)),
            BufferMs = Spread.Of(bufferSamples), PlaybackDelayMs = Spread.Of(delaySamples), Stalls = headless.Driver?.Stalls ?? 0, Resyncs = headless.Driver?.Playback.Resyncs ?? 0, MaxRateAdjustPerMille = maxAdjust,
            RoundTripMs = client.Sync.RoundTripMicros / 1000.0, MatchSeconds = seconds,
            BytesSentPerSecond = seconds > 0 ? (statistics.BytesSent - bytesSentAtStart) / seconds : 0, BytesReceivedPerSecond = seconds > 0 ? (statistics.BytesReceived - bytesReceivedAtStart) / seconds : 0,
            DatagramsSent = statistics.DatagramsSent, DatagramsReceived = statistics.DatagramsReceived, SegmentsRetransmitted = statistics.SegmentsRetransmitted, DuplicatesReceived = statistics.DuplicatesReceived,
            Malformed = statistics.Malformed + endpoint.Malformed, Discarded = client.Discarded, LastDiscard = client.LastDiscard, MaxLagSeenMs = maxLag, MinSpeedSeenPercent = minSpeed,
            Drops = [..client.Dropped.Select(drop => Invariant($"player {drop.Player} ({drop.Reason})"))], MaxFrameMs = maxFrame, MaxTicksInFrame = maxTicksInFrame,
            Gen0 = GC.CollectionCount(0) - gen0, Gen1 = GC.CollectionCount(1) - gen1, Gen2 = GC.CollectionCount(2) - gen2, ManagedMbAtStart = managedStart, ManagedMbAtEnd = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
            WorkingSetMbAtStart = workingStart, WorkingSetMbAtEnd = self.WorkingSet64 / (1024.0 * 1024.0), Build = client.Identity.Id,
        };
        if (resultFile is not null) File.WriteAllText(resultFile, JsonSerializer.Serialize(result, ResultJson));
        socket.Dispose();
        Console.WriteLine(Invariant($"ts={clock.Milliseconds} event=finished name={name} ok={(result.Ok ? "true" : "false")} ticks={result.TicksPlayed} hashChain={result.HashChain} latencyMsMedian={result.LatencyMs.Median:F1} p99={result.LatencyMs.P99:F1} stalls={result.Stalls} problem=\"{problem}\""));
        return result.Ok || disturbed ? 0 : 1;
    }

    // ---------------------------------------------------------------------------------------------------------- harness

    /// <summary>The server of a harness run, as the harness needs it: its lines, and the end of its part in the run.</summary>
    private interface IServerProcess : IDisposable
    {
        string[] Lines { get; }
        string? WaitForValue(string contains, string key, int timeoutMs);
        bool WaitForExit(int timeoutMs);
    }

    /// <summary>
    /// A server that runs already and goes on running (--server HOST:PORT --server-log FILE): the harness plays one room on it. Its lines are the lines of the log
    /// that name this room, and its first line; its "exit" is the line that closes the room.
    /// </summary>
    private sealed class ExternalServer(string logFile, string room, string port) : IServerProcess
    {
        private readonly long _from = File.Exists(logFile) ? new FileInfo(logFile).Length : 0;

        private static IEnumerable<string> Read(string file, long from)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Min(from, stream.Length), SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line) yield return line;
        }

        public string[] Lines
        {
            get
            {
                var mark = $" room={room} ";
                var listening = Read(logFile, 0).FirstOrDefault(line => line.Contains(" event=listening ", StringComparison.Ordinal));
                var mine = Read(logFile, _from).Where(line => line.Contains(mark, StringComparison.Ordinal) || line.EndsWith(mark.TrimEnd(), StringComparison.Ordinal));
                return listening is null ? [..mine] : [listening, ..mine];
            }
        }

        public string? WaitForValue(string contains, string key, int timeoutMs)
        {
            if (contains == "event=listening" && key == "port") return port;
            var watch = Stopwatch.StartNew();
            do
            {
                foreach (var line in Lines)
                    if (line.Contains(contains, StringComparison.Ordinal) && ValueOf(line, key) is { } value) return value;
                Thread.Sleep(50);
            }
            while (watch.ElapsedMilliseconds < timeoutMs);
            return null;
        }

        public bool WaitForExit(int timeoutMs) => WaitForValue(" event=room-closed ", "room", timeoutMs) is not null;

        public void Dispose()
        {
        }
    }

    private sealed class Child : IServerProcess
    {
        private readonly Process _process;
        private readonly StreamWriter _log;
        private readonly List<string> _lines = [];

        /// <param name="linux">Null: a process of this system. Otherwise the process runs in that Linux environment (WSL) with the same binaries; paths are handed over in its form.</param>
        /// <param name="executable">
        /// The file is a program of its own (a self-contained package), not an assembly for "dotnet". Under Linux it is started with an empty environment - no
        /// DOTNET_ROOT, a PATH of the system's directories only - so that it cannot lean on a .NET runtime that happens to be installed for the user.
        /// </param>
        public Child(string dll, IEnumerable<string> arguments, string logFile, LinuxHost? linux = null, bool executable = false)
        {
            _log = new StreamWriter(logFile, false, new UTF8Encoding(false)) { AutoFlush = true };
            var start = new ProcessStartInfo(linux is not null ? "wsl.exe" : executable ? dll : "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            if (linux is not null)
            {
                // --exec: no shell in between, the arguments arrive as they are.
                string[] launcher = executable ? ["/usr/bin/env", "-i", "PATH=/usr/sbin:/usr/bin:/sbin:/bin", "HOME=/tmp"] : [linux.Dotnet];
                foreach (var argument in new[] { "-d", linux.Distribution, "--exec" }.Concat(launcher)) start.ArgumentList.Add(argument);
                dll = LinuxHost.PathOf(dll);
                arguments = arguments.Select(LinuxHost.PathOf);
            }
            if (linux is not null || !executable) start.ArgumentList.Add(dll);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            _process = new Process { StartInfo = start };
            _process.OutputDataReceived += (_, line) => Take(line.Data);
            _process.ErrorDataReceived += (_, line) => Take(line.Data);
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        private void Take(string? line)
        {
            if (line is null) return;
            lock (_lines)
            {
                _lines.Add(line);
                _log.WriteLine(line);
            }
        }

        public string[] Lines
        {
            get { lock (_lines) return [.._lines]; }
        }

        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.HasExited ? _process.ExitCode : -1;

        /// <summary>Waits for a line that contains the text and returns the value of the key on it.</summary>
        public string? WaitForValue(string contains, string key, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                foreach (var line in Lines)
                    if (line.Contains(contains, StringComparison.Ordinal) && ValueOf(line, key) is { } value) return value;
                if (_process.HasExited) return null;
                Thread.Sleep(20);
            }
            return null;
        }

        public bool WaitForExit(int timeoutMs) => _process.WaitForExit(timeoutMs);

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited) _process.Kill(true);
                _process.WaitForExit(5_000);
            }
            catch (InvalidOperationException)
            {
            }
            _process.Dispose();
            lock (_lines) _log.Dispose();
        }
    }

    /// <summary>
    /// A second runtime environment on this machine: a WSL distribution with the .NET runtime. The binaries built under Windows run there unchanged, so a server or a client
    /// under Linux can take part in a match with Windows processes - another operating system and another JIT target on the same CPU.
    /// </summary>
    private sealed record LinuxHost(string Distribution, string Dotnet, string Address)
    {
        /// <summary>A Windows path as the Linux side sees it (C:\a\b becomes /mnt/c/a/b); anything else unchanged.</summary>
        public static string PathOf(string text)
            => text.Length > 2 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '\\' or '/' ? $"/mnt/{char.ToLowerInvariant(text[0])}{text[2..].Replace('\\', '/')}" : text;

        private static string? Run(string distribution, params string[] command)
        {
            try
            {
                var start = new ProcessStartInfo("wsl.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in new[] { "-d", distribution, "--exec" }.Concat(command)) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEnd();
                return process.WaitForExit(20_000) && process.ExitCode == 0 ? output.Replace("\0", string.Empty).Trim() : null;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>The environment, or null with the reason when it is not there (no WSL, no such distribution, no .NET runtime in it).</summary>
        public static LinuxHost? Find(string distribution, out string why, bool needRuntime = true)
        {
            why = string.Empty;
            if (!OperatingSystem.IsWindows()) { why = "this is not Windows"; return null; }
            if (Run(distribution, "/bin/sh", "-c", "echo $HOME") is not { Length: > 0 } home) { why = $"the WSL distribution '{distribution}' does not answer"; return null; }
            var dotnet = home + "/.dotnet/dotnet";
            // A self-contained package brings its runtime: the environment is enough.
            if (needRuntime && (Run(distribution, dotnet, "--list-runtimes") is not { } runtimes || !runtimes.Contains("Microsoft.NETCore.App 10.", StringComparison.Ordinal)))
            {
                why = $"no .NET 10 runtime at {dotnet} in '{distribution}'";
                return null;
            }
            if (Run(distribution, "/bin/hostname", "-I") is not { Length: > 0 } addresses) { why = "the distribution has no network address"; return null; }
            return new LinuxHost(distribution, dotnet, addresses.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
        }
    }

    private static string? ValueOf(string line, string key)
    {
        var at = line.IndexOf(" " + key + "=", StringComparison.Ordinal);
        if (at < 0) return null;
        var start = at + key.Length + 2;
        if (start < line.Length && line[start] == '"')
        {
            var close = line.IndexOf('"', start + 1);
            return close < 0 ? line[(start + 1)..] : line[(start + 1)..close];
        }
        var end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    private sealed record MatchRow(string Label, Impairment Impairment, bool Identical, string Problem, int Clients, int Finished, long Ticks, string HashChain, Spread LatencyMs, double BufferMs, double BufferMinMs,
        double PlaybackDelayMs, long Stalls, long Resyncs, double BytesUpPerSecond, double BytesDownPerSecond, long ClientRetransmissions, long ServerRetransmissions, long ServerUrgentRepeated,
        long ServerUrgentOmitted, double RoundTripMs, string TickIntervals, long ThrottledMs, string Drops, double MaxFrameMs, int Gen2, double ManagedGrowthMb, double WorkingSetGrowthMb, double Seconds)
    {
        /// <summary>What does not fit the table: where the processes ran, a rejoin and its times, the states handed over, a desync and its parts.</summary>
        public string Notes { get; init; } = string.Empty;
    }

    /// <summary>A disturbance that leaves its client in the match: it must play to the end like everybody else.</summary>
    private static bool StaysInMatch(string kind) => kind is "hitch" or "rejoin";

    private static string SelfDll => Path.Combine(AppContext.BaseDirectory, "CnCNew.NetTool.dll");

    private static string ServerDll(string[] args)
    {
        if (Option(args, "--server-dll") is { } stated) return stated;
        // tools/CnCNew.NetTool/bin/<configuration>/net10.0/ -> tools/CnCNew.DedicatedServer/bin/<configuration>/net10.0/
        var directory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var beside = Path.Combine(directory.FullName, "CnCNew.DedicatedServer.dll");
        if (File.Exists(beside)) return beside;
        var built = Path.Combine(directory.Parent!.Parent!.Parent!.Parent!.FullName, "CnCNew.DedicatedServer", "bin", directory.Parent.Name, directory.Name, "CnCNew.DedicatedServer.dll");
        return File.Exists(built) ? built : throw new FileNotFoundException($"The dedicated server is not built ({built}); build tools/CnCNew.DedicatedServer or name it with --server-dll.");
    }

    private static MatchRow RunMatch(string[] args, string label, Impairment impairment, string outDirectory)
    {
        Directory.CreateDirectory(outDirectory);
        var rate = Rate(args);
        var humans = (int)Number(args, "--humans", 2);
        var ais = (int)Number(args, "--ais", 0);
        var watchers = (int)Number(args, "--watchers", 0);
        var members = humans + watchers;
        // Watchers that come when the match is running (the server must admit them: --server-args "--allow-late-watchers").
        var lateWatchers = (int)Number(args, "--late-watchers", 0);
        var lateAfterMs = (int)(Real(args, "--late-after-seconds", 10) * 1000);
        var expectDesync = args.Contains("--expect-desync");
        var target = Ticks(args, rate, 120);
        var mapPath = Option(args, "--map") ?? throw new ArgumentException(Usage);
        var disturbances = (Option(args, "--disturb") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(text => text.Split(':')).ToArray();
        // All clients of a harness come from one address: more than a server lets one address hold by default.
        var serverArguments = new List<string> { "--port", "0", "--allow-modified-builds", "--exit-after-rooms", "1", "--status-seconds", "10", "--max-connections-per-address", "64" };
        serverArguments.AddRange((Option(args, "--server-args") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var watch = Stopwatch.StartNew();
        MatchRow Failed(string problem) => new(label, impairment, false, problem, members, 0, target, string.Empty, Spread.Of([]), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty, 0, string.Empty, 0, 0, 0, 0, watch.Elapsed.TotalSeconds);

        // --linux server,2,5: the server (and with it the proxy) and the clients 2 and 5 run under Linux (WSL); everything else here.
        var linuxParts = (Option(args, "--linux") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        LinuxHost? linux = null;
        if (linuxParts.Length > 0)
        {
            linux = LinuxHost.Find(Option(args, "--linux-distribution") ?? "Debian", out var why, needRuntime: Option(args, "--server-package") is null || linuxParts.Any(part => part != "server"));
            if (linux is null) return Failed($"NOT RUN: no Linux environment ({why})");
        }
        var serverInLinux = linuxParts.Contains("server");
        var linuxClients = linuxParts.Where(part => part != "server").Select(part => int.Parse(part, CultureInfo.InvariantCulture) - 1).ToHashSet();
        // Where the server (or its proxy) is reached: from the side it runs on by the loopback address, from the other side by the address of the Linux environment.
        // A server under Windows is not reachable from the Linux side this way, so Linux clients need the server under Linux.
        if (linuxClients.Count > 0 && !serverInLinux) return Failed("clients under Linux need the server under Linux (--linux server,...)");
        string AddressFor(bool clientInLinux) => serverInLinux && !clientInLinux ? linux!.Address : "127.0.0.1";

        // --server HOST:PORT --server-log FILE: a server that runs already (its log is read for this room's lines); --server-package DIR: the server of a
        // self-contained package instead of the built assembly (scripts/package-server.ps1).
        var external = Option(args, "--server");
        var package = Option(args, "--server-package");
        if (external is not null && (serverInLinux || Option(args, "--server-log") is null)) return Failed("--server needs --server-log FILE and a server on this side");
        using IServerProcess server = external is not null
            ? new ExternalServer(Option(args, "--server-log")!, label, external[(external.LastIndexOf(':') + 1)..])
            : package is not null
                ? new Child(Path.Combine(Path.GetFullPath(package), serverInLinux ? "CnCNew.DedicatedServer" : "CnCNew.DedicatedServer.exe"), serverArguments, Path.Combine(outDirectory, "server.log"),
                    serverInLinux ? linux : null, executable: true)
                : new Child(ServerDll(args), serverArguments, Path.Combine(outDirectory, "server.log"), serverInLinux ? linux : null);
        if (server.WaitForValue("event=listening", "port", 40_000) is not { } serverPort) return Failed("the server did not start");
        // Every client is told the key the server said it has, and takes no other: a harness run is a check of the pinned key too.
        var serverKey = server.WaitForValue("event=listening", "key", 1_000) ?? string.Empty;
        Child? proxy = null;
        var port = serverPort;
        try
        {
            if (!args.Contains("--no-proxy"))
            {
                proxy = new Child(SelfDll,
                [
                    "proxy", "--listen", "0", "--server", $"127.0.0.1:{serverPort}", "--delay", Invariant($"{impairment.DelayMs}"), "--jitter", Invariant($"{impairment.JitterMs}"),
                    "--loss-percent", Invariant($"{impairment.LossPerMille / 10.0}"), "--dup-percent", Invariant($"{impairment.DuplicatePerMille / 10.0}"), "--reorder-percent", Invariant($"{impairment.ReorderPerMille / 10.0}"),
                    "--seed", Option(args, "--seed") ?? "1", "--rate-kbps", Invariant($"{impairment.RateKbps}"), "--queue-kb", Invariant($"{impairment.QueueBytes / 1000}"),
                ], Path.Combine(outDirectory, "proxy.log"), serverInLinux ? linux : null);
                if (proxy.WaitForValue("event=listening", "port", 40_000) is not { } proxyPort) return Failed("the proxy did not start");
                port = proxyPort;
            }
            var clients = new List<Child>();
            try
            {
                var startedAt = Stopwatch.StartNew();
                for (var index = 0; index < members + lateWatchers; index++)
                {
                    if (index == members)
                    {
                        // The match must be running before a latecomer knocks.
                        var until = startedAt.ElapsedMilliseconds + 60_000;
                        while (startedAt.ElapsedMilliseconds < until && !server.Lines.Any(line => line.Contains(" event=match-running ", StringComparison.Ordinal))) Thread.Sleep(50);
                        Thread.Sleep(lateAfterMs);
                    }
                    var arguments = new List<string>
                    {
                        "client", linuxClients.Contains(index) ? LinuxHost.PathOf(Path.GetFullPath(args[1])) : args[1], "--server", $"{AddressFor(linuxClients.Contains(index))}:{port}", "--room", label, "--name", $"c{index + 1}", "--seat", Invariant($"{(index < humans ? index : -1)}"), "--members", Invariant($"{members}"),
                        "--map", mapPath, "--humans", Invariant($"{humans}"), "--ais", Invariant($"{ais}"), "--ticks", Invariant($"{target}"), "--tick-rate", Invariant($"{rate.TicksPerSecond}"),
                        "--result", Path.GetFullPath(Path.Combine(outDirectory, $"client-{index + 1}.json")), "--server-key", Option(args, "--client-server-key") ?? serverKey,
                    };
                    if (Option(args, "--room-password") is { } password) arguments.AddRange(["--room-password", password]);
                    if (index >= members) arguments.Add("--watch");
                    if (expectDesync) arguments.AddRange(["--diagnostics", Path.GetFullPath(Path.Combine(outDirectory, "packages", $"client-{index + 1}"))]);
                    foreach (var disturbance in disturbances.Where(parts => parts.Length >= 3 && int.Parse(parts[0], CultureInfo.InvariantCulture) == index))
                    {
                        switch (disturbance[1])
                        {
                            case "leave": arguments.AddRange(["--leave-at-tick", disturbance[2]]); break;
                            case "vanish": arguments.AddRange(["--vanish-at-tick", disturbance[2]]); break;
                            case "freeze": arguments.AddRange(["--freeze-at-tick", disturbance[2]]); break;
                            case "slow": arguments.AddRange(["--slow-from-tick", disturbance[2], "--slow-ms", disturbance.Length > 3 ? disturbance[3] : "25"]); break;
                            case "hitch": arguments.AddRange(["--hitch-at-tick", disturbance[2], "--hitch-ms", disturbance.Length > 3 ? disturbance[3] : "500"]); break;
                            case "rejoin": arguments.AddRange(["--rejoin-at-tick", disturbance[2], "--rejoin-after-ms", disturbance.Length > 3 ? disturbance[3] : "2000"]); break;
                            case "corrupt": arguments.AddRange(["--corrupt-at-tick", disturbance[2]]); break;
                            default: throw new ArgumentException($"Unknown disturbance '{disturbance[1]}' (leave, vanish, freeze, slow, hitch, rejoin, corrupt).");
                        }
                    }
                    clients.Add(new Child(SelfDll, arguments, Path.Combine(outDirectory, $"client-{index + 1}.log"), linuxClients.Contains(index) ? linux : null));
                    // The first one opens the room and is its host; the others follow at once.
                    if (index == 0) Thread.Sleep(300);
                }
                var limit = (int)Math.Min(int.MaxValue, (target / rate.TicksPerSecond * 4 + 400) * 1000);
                var waiting = Stopwatch.StartNew();
                var disturbed = disturbances.Where(parts => parts.Length >= 3 && !StaysInMatch(parts[1])).Select(parts => int.Parse(parts[0], CultureInfo.InvariantCulture)).ToHashSet();
                long othersDoneAt = -1;
                while (clients.Any(client => !client.HasExited) && waiting.ElapsedMilliseconds < limit)
                {
                    Thread.Sleep(200);
                    // A client that was frozen or made too slow may never end by itself: when all the others are done it is given ten seconds and then stopped.
                    if (othersDoneAt < 0 && Enumerable.Range(0, clients.Count).All(index => disturbed.Contains(index) || clients[index].HasExited)) othersDoneAt = waiting.ElapsedMilliseconds;
                    if (othersDoneAt >= 0 && waiting.ElapsedMilliseconds - othersDoneAt > 10_000) break;
                }
            }
            finally
            {
                foreach (var client in clients) client.Dispose();
            }
            server.WaitForExit(external is null ? 15_000 : 40_000);
        }
        finally
        {
            proxy?.Dispose();
        }

        var results = new List<ClientResult>();
        for (var index = 0; index < members + lateWatchers; index++)
        {
            var file = Path.Combine(outDirectory, $"client-{index + 1}.json");
            if (File.Exists(file) && JsonSerializer.Deserialize<ClientResult>(File.ReadAllText(file), ResultJson) is { } result) results.Add(result);
        }
        var serverLines = server.Lines;
        long Sum(string name, string key) => serverLines.Where(line => line.Contains(" event=" + name + " ", StringComparison.Ordinal)).Sum(line => long.TryParse(ValueOf(line, key), CultureInfo.InvariantCulture, out var value) ? value : 0);
        var closed = serverLines.LastOrDefault(line => line.Contains(" event=room-closed ", StringComparison.Ordinal)) ?? string.Empty;
        var undisturbed = Enumerable.Range(0, members + lateWatchers).Where(index => !disturbances.Any(parts => parts.Length >= 3 && int.Parse(parts[0], CultureInfo.InvariantCulture) == index && !StaysInMatch(parts[1]))).ToArray();
        members += lateWatchers;
        var finished = results.Where(result => result.Ok).ToList();
        var problems = new List<string>();
        if (results.Count < members) problems.Add($"{members - results.Count} client(s) wrote no result");
        foreach (var result in results.Where(result => !result.Ok && result.Disturbance.Length == 0)) problems.Add($"{result.Name}: {(result.Problem.Length > 0 ? result.Problem : $"played {result.TicksPlayed} of {result.TargetTick}")}");
        if (finished.Count < undisturbed.Length) problems.Add($"only {finished.Count} of {undisturbed.Length} undisturbed clients played to the end");
        // The chain is over every tick from the first: whoever joined a running match has a chain of its own, and is compared by the state at the end.
        var fromStart = finished.Where(result => result.JoinedAtTick == 0 && result.RejoinGoneAtTick < 0).ToList();
        if (fromStart.Select(result => result.HashChain).Distinct().Count() > 1) problems.Add("the clients' hash chains differ: " + string.Join(" ", fromStart.Select(result => $"{result.Name}={result.HashChain}")));
        if (finished.Select(result => result.FinalSessionHash).Distinct().Count() > 1) problems.Add("the clients' final session hashes differ: " + string.Join(" ", finished.Select(result => $"{result.Name}={result.FinalSessionHash}")));
        var desyncLine = serverLines.FirstOrDefault(line => line.Contains(" event=desync ", StringComparison.Ordinal));
        if (desyncLine is not null && !expectDesync) problems.Add("the server reported a desync");
        var notes = new List<string>();
        // Where every process ran, by what it says itself.
        var serverOs = ValueOf(serverLines.FirstOrDefault(line => line.Contains(" event=listening ", StringComparison.Ordinal)) ?? string.Empty, "os") ?? string.Empty;
        if (linux is not null)
        {
            static bool IsLinux(string os) => os.Contains("Linux", StringComparison.OrdinalIgnoreCase);
            if (IsLinux(serverOs) != serverInLinux) problems.Add($"the server ran on '{serverOs}'");
            foreach (var result in results)
            {
                var index = int.Parse(result.Name[1..], CultureInfo.InvariantCulture) - 1;
                if (IsLinux(result.Os) != linuxClients.Contains(index)) problems.Add($"{result.Name} ran on '{result.Os}'");
            }
            notes.Add($"server on {(IsLinux(serverOs) ? "Linux" : "Windows")}; clients " + string.Join(' ', results.Select(result => $"{result.Name}:{(IsLinux(result.Os) ? "Linux" : "Windows")}")));
        }
        foreach (var result in results.Where(result => result.RejoinGoneAtTick >= 0))
        {
            notes.Add(Invariant($"{result.Name} lost its connection at tick {result.RejoinGoneAtTick} and joined again at tick {result.JoinedAtTick}: state {result.RejoinStateBytes} B on the wire, loaded after {result.RejoinLoadedMs:F0} ms, at the head of the match after {result.RejoinCaughtUpMs:F0} ms"));
            if (result.Ok && result.JoinedAtTick == 0) problems.Add($"{result.Name} did not join the running match again");
        }
        // The same as the server saw it: from the token to the state being passed on, to the joiner standing at it, to the joiner playing in time again.
        foreach (var returned in serverLines.Where(line => line.Contains(" event=seat-returned ", StringComparison.Ordinal)))
        {
            var connection = ValueOf(returned, "connection");
            long At(string line) => long.TryParse(ValueOf(" " + line, "ts"), CultureInfo.InvariantCulture, out var ts) ? ts : 0;
            var from = At(returned);
            var relayed = serverLines.FirstOrDefault(line => line.Contains(" event=snapshot-relayed ", StringComparison.Ordinal) && ValueOf(line, "to") == connection);
            var joined = serverLines.FirstOrDefault(line => line.Contains(" event=joined-late ", StringComparison.Ordinal) && ValueOf(line, "connection") == connection);
            var caught = serverLines.FirstOrDefault(line => line.Contains(" event=caught-up ", StringComparison.Ordinal) && ValueOf(line, "connection") == connection && At(line) >= (joined is null ? from : At(joined)));
            notes.Add(Invariant($"server: player {ValueOf(returned, "player")} back at tick {ValueOf(returned, "tick")}; state passed on after {(relayed is null ? -1 : At(relayed) - from)} ms, joiner at that state after {(joined is null ? -1 : At(joined) - from)} ms ({(joined is null ? "?" : (long.Parse(ValueOf(joined, "tick")!, CultureInfo.InvariantCulture) - long.Parse(ValueOf(joined, "fromTick")!, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture))} ticks behind), in time again after {(caught is null ? -1 : At(caught) - from)} ms"));
        }
        foreach (var result in results.Where(result => result.RejoinGoneAtTick < 0 && result.JoinedAtTick > 0))
            notes.Add(Invariant($"{result.Name} joined the running match at tick {result.JoinedAtTick}"));
        foreach (var result in results.Where(result => result.SnapshotsOffered > 0))
            notes.Add(Invariant($"{result.Name} handed over {result.SnapshotsOffered} state(s): {result.SnapshotStateBytes} B, packed {result.SnapshotPackedBytes} B, in {result.SnapshotMs:F1} ms"));
        if (expectDesync)
        {
            // The match is meant to break: the server must say so at the tick of the change, name the parts, and every client must have written its package.
            problems.Clear();
            var corrupted = results.FirstOrDefault(result => result.Corrupted.Length > 0);
            var partsLine = serverLines.LastOrDefault(line => line.Contains(" event=desync-parts ", StringComparison.Ordinal));
            if (desyncLine is null) problems.Add("the server reported no desync");
            if (corrupted is null) problems.Add("no client changed its state");
            if (partsLine is null) problems.Add("the server named no parts");
            if (results.Count(result => result.Package.Length > 0) < results.Count) problems.Add($"only {results.Count(result => result.Package.Length > 0)} of {results.Count} clients wrote a package");
            notes.Add($"changed: {corrupted?.Corrupted}; desync reported at tick {ValueOf(desyncLine ?? string.Empty, "tick")}; parts: {ValueOf(partsLine ?? string.Empty, "parts")}");
        }
        if (finished.Any(result => result.Discarded > 0 || result.Malformed > 0)) problems.Add("a client discarded messages or datagrams");
        var lastAgreed = long.TryParse(ValueOf(closed, "lastAgreedTick"), CultureInfo.InvariantCulture, out var agreed) ? agreed : -1;
        if (finished.Count > 0 && lastAgreed < target - 1 && !expectDesync) problems.Add($"the server agreed on the hashes only up to tick {lastAgreed} of {target - 1}");
        // Over the clients that gave orders: the mean of their medians and 95 % values, the worst of their 99 % values.
        var ordering = finished.Where(result => result.LatencyMs.Count > 0).ToList();
        var pooled = ordering.Count == 0 ? Spread.Of([]) : new Spread(ordering.Sum(result => result.LatencyMs.Count), ordering.Average(result => result.LatencyMs.Mean), ordering.Min(result => result.LatencyMs.Min),
            ordering.Average(result => result.LatencyMs.Median), ordering.Average(result => result.LatencyMs.P95), ordering.Max(result => result.LatencyMs.P99), ordering.Max(result => result.LatencyMs.Max));
        double Mean(Func<ClientResult, double> value) => finished.Count == 0 ? 0 : finished.Average(value);
        return new MatchRow(label, impairment, problems.Count == 0, string.Join("; ", problems), members, finished.Count, target, finished.FirstOrDefault()?.HashChain ?? string.Empty, pooled,
            Mean(result => result.BufferMs.Mean), finished.Count == 0 ? 0 : finished.Min(result => result.BufferMs.Min), Mean(result => result.PlaybackDelayMs.Mean), finished.Sum(result => result.Stalls),
            finished.Sum(result => result.Resyncs), Mean(result => result.BytesSentPerSecond), Mean(result => result.BytesReceivedPerSecond), finished.Sum(result => result.SegmentsRetransmitted),
            Sum("disconnected", "segmentsRetransmitted"), Sum("disconnected", "urgentRepeated"), Sum("disconnected", "urgentOmitted"), Mean(result => result.RoundTripMs), ValueOf(closed, "tickIntervals") ?? string.Empty,
            long.TryParse(ValueOf(closed, "throttledMs"), CultureInfo.InvariantCulture, out var throttled) ? throttled : 0,
            string.Join(" ", serverLines.Where(line => line.Contains(" event=left ", StringComparison.Ordinal) && ValueOf(line, "phase") == "Match").Select(line => $"player{ValueOf(line, "player")}:{ValueOf(line, "reason")}@{ValueOf(line, "tick")}")),
            finished.Count == 0 ? 0 : finished.Max(result => result.MaxFrameMs), finished.Sum(result => result.Gen2), finished.Count == 0 ? 0 : finished.Max(result => result.ManagedMbAtEnd - result.ManagedMbAtStart),
            finished.Count == 0 ? 0 : finished.Max(result => result.WorkingSetMbAtEnd - result.WorkingSetMbAtStart), watch.Elapsed.TotalSeconds) { Notes = string.Join("\n", notes) };
    }

    private static string Describe(MatchRow row)
        => Invariant($"{(row.Identical ? "ok  " : "FAIL")} {row.Label}: delay {row.Impairment.DelayMs} ms, jitter {row.Impairment.JitterMs} ms, loss {row.Impairment.LossPerMille / 10.0} % - {row.Finished} of {row.Clients} clients played {row.Ticks} ticks, hash chain {row.HashChain}; order to execution median {row.LatencyMs.Median:F0} / 95 % {row.LatencyMs.P95:F0} / 99 % {row.LatencyMs.P99:F0} ms ({row.LatencyMs.Count} orders); round trip {row.RoundTripMs:F0} ms, playback delay {row.PlaybackDelayMs:F0} ms, buffer mean {row.BufferMs:F0} ms; {row.Stalls} waits, {row.Resyncs} resyncs; {row.BytesUpPerSecond:F0} B/s up and {row.BytesDownPerSecond:F0} B/s down per client; repetitions: clients {row.ClientRetransmissions}, server {row.ServerRetransmissions} segments and {row.ServerUrgentRepeated} bundles ({row.ServerUrgentOmitted} omitted); slowed {row.ThrottledMs} ms; left: [{row.Drops}]; longest client frame {row.MaxFrameMs:F0} ms; gen-2 {row.Gen2}; memory +{row.ManagedGrowthMb:F0} MB managed, +{row.WorkingSetGrowthMb:F0} MB working set; server ticks {row.TickIntervals} [{row.Seconds:F0} s]{(row.Identical ? string.Empty : " :: " + row.Problem)}");

    public static int NetMatch(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException(Usage);
        var outDirectory = Option(args, "--out") ?? throw new ArgumentException("--out names the directory for logs and results.");
        var row = RunMatch(args, Option(args, "--label") ?? "match", ImpairmentOf(args), outDirectory);
        foreach (var note in row.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Console.WriteLine("     " + note);
        Console.WriteLine(Describe(row));
        File.WriteAllText(Path.Combine(outDirectory, "row.json"), JsonSerializer.Serialize(row, ResultJson));
        // 3: the match could not be set up as asked (no Linux environment) - not run, which is neither passed nor failed.
        return row.Identical ? 0 : row.Problem.StartsWith("NOT RUN", StringComparison.Ordinal) ? 3 : 1;
    }

    public static int NetMatrix(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException(Usage);
        var outDirectory = Option(args, "--out") ?? throw new ArgumentException("--out names the directory for logs and results.");
        Directory.CreateDirectory(outDirectory);
        int[] List(string name, string fallback) => [..(Option(args, name) ?? fallback).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(text => int.Parse(text, CultureInfo.InvariantCulture))];
        var lines = Option(args, "--lines") is { } stated
            ? stated.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(text => text.Split('/')).Select(parts => (Delay: int.Parse(parts[0], CultureInfo.InvariantCulture), Jitter: int.Parse(parts[1], CultureInfo.InvariantCulture), Loss: int.Parse(parts[2], CultureInfo.InvariantCulture))).ToArray()
            : (from delay in List("--delays", "10,40,80,150") from jitter in List("--jitters", "0,10,30") from loss in List("--losses", "0,1,3") select (Delay: delay, Jitter: jitter, Loss: loss)).ToArray();
        var table = new StringBuilder();
        table.AppendLine("delay_ms\tjitter_ms\tloss_percent\tidentical\tclients\tfinished\tticks\thash_chain\tlatency_median_ms\tlatency_p95_ms\tlatency_p99_ms\torders\tround_trip_ms\tplayback_delay_ms\tbuffer_mean_ms\tbuffer_min_ms\twaits\tresyncs\tbytes_up_per_s\tbytes_down_per_s\tclient_retransmissions\tserver_retransmissions\tserver_bundles_repeated\tthrottled_ms\tmax_frame_ms\tgen2\tproblem");
        var failed = 0;
        foreach (var line in lines)
        {
            var label = $"d{line.Delay}-j{line.Jitter}-l{line.Loss}";
            var row = RunMatch(args, label, new Impairment(line.Delay, line.Jitter, line.Loss * 10), Path.Combine(outDirectory, label));
            Console.WriteLine(Describe(row));
            if (!row.Identical) failed++;
            table.AppendLine(Invariant($"{line.Delay}\t{line.Jitter}\t{line.Loss}\t{(row.Identical ? "yes" : "NO")}\t{row.Clients}\t{row.Finished}\t{row.Ticks}\t{row.HashChain}\t{row.LatencyMs.Median:F1}\t{row.LatencyMs.P95:F1}\t{row.LatencyMs.P99:F1}\t{row.LatencyMs.Count}\t{row.RoundTripMs:F1}\t{row.PlaybackDelayMs:F1}\t{row.BufferMs:F1}\t{row.BufferMinMs:F1}\t{row.Stalls}\t{row.Resyncs}\t{row.BytesUpPerSecond:F0}\t{row.BytesDownPerSecond:F0}\t{row.ClientRetransmissions}\t{row.ServerRetransmissions}\t{row.ServerUrgentRepeated}\t{row.ThrottledMs}\t{row.MaxFrameMs:F1}\t{row.Gen2}\t{row.Problem}"));
            File.WriteAllText(Path.Combine(outDirectory, "matrix.tsv"), table.ToString());
        }
        Console.WriteLine($"Matrix: {lines.Length - failed} of {lines.Length} lines with identical hashes on all clients; table in {Path.Combine(outDirectory, "matrix.tsv")}.");
        return failed == 0 ? 0 : 1;
    }

    // ----------------------------------------------------------------------------------------------------------- pacing

    /// <summary>
    /// How evenly this machine wakes a loop that waits for the next tick (docs/NETWORK-PROTOCOL.md section 13.4): the deviation of the wake-up from the time asked for, for a
    /// tick of 60 Hz, with a plain sleep, a wait on a socket, and the same wait while the process holds the fine timer.
    /// </summary>
    public static int Pacing(string[] args)
    {
        var seconds = Number(args, "--seconds", 5);
        const long TickMicros = 16_666;
        using var socket = new UdpSocketChannel(new IPEndPoint(IPAddress.Loopback, 0));
        (string Name, Action<long> Wait, bool Fine)[] ways =
        [
            ("Thread.Sleep(remaining ms)", micros => Thread.Sleep((int)Math.Max(1, micros / 1000)), false),
            ("socket wait (select with timeout)", micros => socket.WaitReadable((int)micros), false),
            ("socket wait, fine timer held", micros => socket.WaitReadable((int)micros), true),
            ("socket wait in steps of 2 ms, fine timer held (the server's loop)", micros => socket.WaitReadable((int)Math.Min(micros, 2_000)), true),
        ];
        Console.WriteLine($"Waking a loop every {TickMicros} us for {seconds} s per way; {(OperatingSystem.IsWindows() ? "Windows" : "not Windows")}, {Environment.ProcessorCount} processors.");
        foreach (var (name, wait, fine) in ways)
        {
            using var timer = fine ? new FineTimer() : null;
            var clock = new SystemNetClock();
            var late = new List<double>();
            var next = TickMicros;
            while (clock.Microseconds < seconds * 1_000_000)
            {
                var now = clock.Microseconds;
                if (now < next)
                {
                    wait(next - now);
                    continue;
                }
                late.Add((now - next) / 1000.0);
                next += TickMicros;
                // A loop that woke more than a tick late does not owe the ticks it slept through.
                if (now - next > TickMicros) next = now + TickMicros;
            }
            var spread = Spread.Of(late);
            Console.WriteLine(Invariant($"  {name,-68} {spread.Count,4} wake-ups: late by mean {spread.Mean:F2} ms, median {spread.Median:F2}, 95 % {spread.P95:F2}, 99 % {spread.P99:F2}, max {spread.Max:F2} ms"));
        }
        return 0;
    }
}

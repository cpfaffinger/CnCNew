using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;

/// <summary>
/// The load generator of phase 28 (docs/DEDICATED-SERVER.md section 11): rooms of members that speak the whole protocol over real sockets and simulate nothing. The
/// relay never simulates either, so to the server such a member is a client: it says hello through the sealed transport, takes a seat, loads, reports a hash for every
/// tick, gives orders, pings, ends the match - and, when asked to, vanishes, leaves, or comes back with its token and is handed a state. What it cannot show is
/// anything about a client: its hash is a constant, its "state" a block of random bytes of the size a real one has.
/// </summary>
static partial class NetTool
{
    private const string LoadDefinitions = "10AD10AD10AD10AD10AD10AD10AD10AD10AD10AD10AD10AD10AD10AD10AD10AD";
    private const ulong LoadHash = 7;

    private sealed class LoadTotals
    {
        public long MatchesStarted, MatchesFinished, MatchesAbandoned, Stuck, Desyncs, Refused, UnexpectedDrops, Discards, TicksPlayed, Orders, OrdersSeen, Vanished, Left, Rejoins, RejoinsBack, StatesOffered;
        public long BytesSent, BytesReceived, MemberMicros;
        public readonly List<double> LatencyMs = [];
        public readonly List<string> Problems = [];

        public void Problem(string text)
        {
            lock (Problems)
            {
                if (Problems.Count < 200) Problems.Add(text);
            }
        }
    }

    private sealed class LoadMember(int index) : IDisposable
    {
        public int Index { get; } = index;
        public UdpSocketChannel? Socket;
        public UdpClientEndpoint? Endpoint;
        public LockstepClient? Client;
        public bool Seated, Loaded, Gone, EndReported, CountedDrop, Returning;
        public long ReportedThrough = -1, ConnectedAtMicros, MatchAtMicros = -1, BackAtMicros = -1;
        public ulong Token, NextReceipt = 1;
        public double OrderDebt;
        public readonly Dictionary<ulong, long> SentAt = [];

        public void Dispose()
        {
            Endpoint?.Dispose();
            Socket?.Dispose();
            Endpoint = null;
            Socket = null;
            Client = null;
        }
    }

    private sealed class LoadRoom(int number)
    {
        public int Number { get; } = number;
        public int Round;
        public string Name = string.Empty;
        public LoadMember[] Members = [];
        /// <summary>Members of the round before, on their way out: their goodbye and the close of their connection are still being delivered.</summary>
        public LoadMember[] Leaving = [];
        public long LeavingUntilMicros;
        public long OpenedAtMicros, StartRequestedAt = long.MinValue, MatchAtMicros = -1, EndedAtMicros = -1;
        public bool Configured;
        public (int Member, long Tick, int Kind, long BackAfterMicros) Churn = (-1, -1, 0, 0);
        public Random Random = new(1);
    }

    /// <summary>
    /// seal-cost [--datagrams N] [--handshakes N]: what the sealed transport costs this machine (docs/NETWORK-PROTOCOL.md section 21.2) - the cryptography of a
    /// handshake (both ends), and sealing and opening a datagram at the sizes a match sends, against copying the same bytes.
    /// </summary>
    public static int SealCost(string[] args)
    {
        var datagrams = (int)Number(args, "--datagrams", 400_000);
        var handshakes = (int)Number(args, "--handshakes", 2_000);
        using var identity = UdpIdentity.Create();
        for (var index = 0; index < 50; index++)
        {
            var (warmClient, warmServer) = UdpCrypto.Exchange(identity, 1, 2);
            warmClient.Dispose();
            warmServer.Dispose();
        }
        var watch = Stopwatch.StartNew();
        for (var index = 0; index < handshakes; index++)
        {
            var (one, other) = UdpCrypto.Exchange(identity, (ulong)index, 99);
            one.Dispose();
            other.Dispose();
        }
        var perHandshake = watch.Elapsed.TotalMilliseconds * 1000 / handshakes;
        Console.WriteLine(Invariant($"handshake: {perHandshake:F0} us of cryptography for both ends together (two key pairs, a signature, its check, two agreements; {handshakes} handshakes) - {1_000_000 / perHandshake:F0} per second on one core"));

        // The server's share of it, with the same calls of the base class library: a key pair of its own, the agreement with the client's key, its signature.
        using (var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        using (var peer = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
        {
            var said = new byte[226];
            var signature = new byte[64];
            watch.Restart();
            for (var index = 0; index < handshakes; index++)
            {
                using var own = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                var secret = own.DeriveRawSecretAgreement(peer.PublicKey);
                HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, new byte[64], SHA256.HashData(said), said.AsSpan(0, 18));
                signer.TrySignData(said, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation, out _);
            }
            var perServer = watch.Elapsed.TotalMilliseconds * 1000 / handshakes;
            Console.WriteLine(Invariant($"handshake, the server's share: {perServer:F0} us (a key pair, an agreement, a signature) - {1_000_000 / perServer:F0} connections per second would take one core"));
        }

        var (client, server) = UdpCrypto.Exchange(identity, 5, 6);
        var plain = new byte[UdpFormat.MaxPlainBytes];
        RandomNumberGenerator.Fill(plain);
        var wire = new byte[UdpFormat.MaxDatagramBytes];
        var opened = new byte[UdpFormat.MaxDatagramBytes];
        Console.WriteLine("plain_bytes\twire_bytes\tseal_us\topen_us\tcopy_us\tseal_and_open_us");
        foreach (var size in new[] { 40, 80, 160, 320, 640, UdpFormat.MaxPlainBytes })
        {
            double Measure(Action<int> body)
            {
                for (var index = 0; index < 20_000; index++) body(index);
                var time = Stopwatch.StartNew();
                for (var index = 0; index < datagrams; index++) body(index);
                return time.Elapsed.TotalMilliseconds * 1000 / datagrams;
            }
            var sealedLength = 0;
            var seal = Measure(_ => sealedLength = client.Seal(plain.AsSpan(0, size), wire));
            // Every datagram is opened once: sealed afresh for that, and the sealing taken off.
            var both = Measure(_ =>
            {
                var length = client.Seal(plain.AsSpan(0, size), wire);
                if (!server.TryOpen(wire.AsSpan(0, length), opened, out var got) || got != size) throw new InvalidOperationException("A sealed datagram did not open.");
            });
            var copy = Measure(_ => plain.AsSpan(0, size).CopyTo(wire));
            Console.WriteLine(Invariant($"{size}\t{sealedLength}\t{seal:F3}\t{both - seal:F3}\t{copy:F4}\t{both:F3}"));
        }
        Console.WriteLine(Invariant($"every datagram is {UdpFormat.SealBytes} bytes longer (an 8-byte number and a 16-byte tag); forged {server.Forged}, replayed {server.Replayed}"));
        client.Dispose();
        server.Dispose();
        return 0;
    }

    /// <summary>
    /// load --server HOST:PORT [--server-key FINGERPRINT] --rooms N [--members 8] [--watchers 0] [--seconds S] [--match-seconds M] [--orders-per-second R]
    /// [--churn-percent P] [--state-kb K] [--threads T] [--tick-rate 60] [--seed N] [--label TEXT] [--out file.json] [--room-password TEXT]
    /// </summary>
    public static int Load(string[] args)
    {
        var address = Option(args, "--server") ?? throw new ArgumentException("load needs --server HOST:PORT.");
        var server = IPEndPoint.Parse(address);
        var rooms = (int)Number(args, "--rooms", 1);
        var members = (int)Number(args, "--members", 8);
        var watchers = (int)Number(args, "--watchers", 0);
        var seconds = Real(args, "--seconds", 60);
        var matchSeconds = Real(args, "--match-seconds", 120);
        var ordersPerSecond = Real(args, "--orders-per-second", 1.5);
        var churnPercent = Real(args, "--churn-percent", 0);
        var stateBytes = (int)Number(args, "--state-kb", 200) * 1024;
        var threads = (int)Number(args, "--threads", Math.Clamp(rooms / 8, 1, Math.Max(1, Environment.ProcessorCount / 2)));
        var rate = (int)Number(args, "--tick-rate", 60);
        var seed = (int)Number(args, "--seed", 1);
        var label = Option(args, "--label") ?? "load";
        var pinned = (Option(args, "--server-key") ?? string.Empty).ToUpperInvariant();
        var password = Option(args, "--room-password") ?? string.Empty;
        var seats = Math.Min(8, members - watchers);
        if (seats < 1 || members > Protocol.MaxConnections) throw new ArgumentException("load: at least one seated member, and no more members than a room admits.");
        var matchTicks = (long)(matchSeconds * rate);
        var slots = Enumerable.Range(0, 8).Select(index => new MatchSlotDescription(index < seats ? MatchSlotKind.Human : MatchSlotKind.Computer, "FactionAmerica", StartPosition: index)).ToArray();
        var settings = new RoomSettings(rate, "Maps/Load/Load.map", "10AD", matchTicks, 10_000, 0, 0, [..slots]);
        var state = RandomNumberGenerator.GetBytes(stateBytes);

        var clock = new SystemNetClock();
        using var timer = new FineTimer();
        var totals = new LoadTotals();
        var endAt = (long)(seconds * 1_000_000);
        var all = Enumerable.Range(0, rooms).Select(number => new LoadRoom(number) { Random = new Random(seed * 7919 + number) }).ToArray();

        void Open(LoadRoom room, long now)
        {
            foreach (var member in room.Members) member.Dispose();
            room.Round++;
            room.Name = Invariant($"{label}-{room.Number}-{room.Round}");
            room.Members = [..Enumerable.Range(0, members).Select(index => new LoadMember(index))];
            room.OpenedAtMicros = now;
            room.StartRequestedAt = long.MinValue;
            room.MatchAtMicros = -1;
            room.EndedAtMicros = -1;
            room.Configured = false;
            room.Churn = (-1, -1, 0, 0);
            if (seats > 1 && room.Random.NextDouble() * 100 < churnPercent)
            {
                // One player other than the host, somewhere in the middle: it vanishes (0), says it leaves (1), or vanishes and comes back (2).
                // Somebody who is to come back goes early, so that there is a match left to come back to.
                var kind = room.Random.Next(3);
                room.Churn = (1 + room.Random.Next(seats - 1), matchTicks / 5 + room.Random.NextInt64(Math.Max(1, kind == 2 ? matchTicks / 5 : matchTicks / 2)), kind, 2_000_000 + room.Random.Next(3_000_000));
            }
            // The host first; the others when it is in (whoever names a room first is its host).
            Connect(room, room.Members[0], now, 0);
        }

        void Connect(LoadRoom room, LoadMember member, long now, ulong token)
        {
            member.Socket = new UdpSocketChannel(new IPEndPoint(server.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
            member.Endpoint = new UdpClientEndpoint(member.Socket, server, clock, BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)), trust: pinned.Length == 0 ? null : key => UdpCrypto.FingerprintOf(key) == pinned);
            member.Client = new LockstepClient(member.Endpoint.Connection, Invariant($"m{member.Index}"), LoadDefinitions, room: room.Name, clock: clock, rejoinToken: token,
                watch: member.Index >= seats, roomPassword: password);
            member.Client.SnapshotRequested = request =>
            {
                // The state "at" the tick after the last one reported; the relay passes it on when that hash is the agreed one.
                member.Client.OfferSnapshot(request, new ResumeState(member.ReportedThrough + 1, LoadHash, [], state));
                Interlocked.Increment(ref totals.StatesOffered);
            };
            member.ConnectedAtMicros = now;
            member.Seated = member.Loaded = member.Gone = member.EndReported = member.CountedDrop = false;
            member.Returning = token != 0;
            member.ReportedThrough = -1;
            member.MatchAtMicros = -1;
        }

        void Account(LoadMember member)
        {
            if (member.Endpoint is null) return;
            Interlocked.Add(ref totals.BytesSent, member.Endpoint.Link.Statistics.BytesSent);
            Interlocked.Add(ref totals.BytesReceived, member.Endpoint.Link.Statistics.BytesReceived);
            Interlocked.Add(ref totals.Discards, member.Client?.Discarded ?? 0);
            if (member.MatchAtMicros >= 0) Interlocked.Add(ref totals.MemberMicros, clock.Microseconds - member.MatchAtMicros);
        }

        void Close(LoadRoom room, bool finished, string? problem)
        {
            foreach (var member in room.Members)
            {
                if (member.Client is { IsOpen: true } client)
                {
                    // A goodbye and a close, as a client that goes home: the server need not wait for a timeout to know the connection is over.
                    client.Leave();
                    client.Close();
                    member.Endpoint!.Flush();
                }
                Account(member);
            }
            room.Leaving = room.Members;
            room.LeavingUntilMicros = clock.Microseconds + 400_000;
            if (finished) Interlocked.Increment(ref totals.MatchesFinished);
            else if (problem is not null)
            {
                Interlocked.Increment(ref totals.Stuck);
                totals.Problem($"{room.Name}: {problem}");
            }
            else if (room.MatchAtMicros >= 0) Interlocked.Increment(ref totals.MatchesAbandoned);
            room.Members = [];
        }

        void Step(LoadRoom room, long now, bool running)
        {
            if (room.Leaving.Length > 0)
            {
                foreach (var member in room.Leaving)
                {
                    member.Endpoint?.Pump();
                    member.Endpoint?.Flush();
                }
                if (now < room.LeavingUntilMicros) return;
                foreach (var member in room.Leaving) member.Dispose();
                room.Leaving = [];
            }
            if (room.Members.Length == 0)
            {
                if (running) Open(room, now);
                return;
            }
            var host = room.Members[0];
            foreach (var member in room.Members)
            {
                if (member.Gone)
                {
                    // Somebody who vanished comes back with its token when its time is over.
                    if (member.BackAtMicros >= 0 && now >= member.BackAtMicros)
                    {
                        member.BackAtMicros = -1;
                        member.Dispose();
                        Connect(room, member, now, member.Token);
                        Interlocked.Increment(ref totals.Rejoins);
                    }
                    continue;
                }
                if (member.Endpoint is null)
                {
                    if (host.Client is { Phase: ClientPhase.InRoom }) Connect(room, member, now, 0);
                    continue;
                }
                member.Endpoint.Pump();
                var client = member.Client!;
                client.Pump();
                if (client.Rejection is { } rejection && !member.CountedDrop)
                {
                    member.CountedDrop = true;
                    Interlocked.Increment(ref totals.Refused);
                    totals.Problem($"{room.Name} m{member.Index}: refused ({rejection.Reason}: {rejection.Text})");
                }
                if (client.Desync is not null && !member.CountedDrop)
                {
                    member.CountedDrop = true;
                    Interlocked.Increment(ref totals.Desyncs);
                    totals.Problem($"{room.Name} m{member.Index}: told of a desync at tick {client.Desync.Tick}");
                }
                if (!client.IsOpen && client.End is null && !member.CountedDrop)
                {
                    member.CountedDrop = true;
                    Interlocked.Increment(ref totals.UnexpectedDrops);
                    totals.Problem($"{room.Name} m{member.Index}: the connection closed in phase {client.Phase} ({member.Endpoint.Link.CloseReason})");
                }
                switch (client.Phase)
                {
                    case ClientPhase.InRoom:
                        if (member.Returning) break;
                        if (client.IsHost && !room.Configured)
                        {
                            client.Configure(settings);
                            room.Configured = true;
                        }
                        if (!member.Seated && client.Room is { } shown && shown.Settings.Slots.Length == settings.Slots.Length)
                        {
                            if (member.Index < seats)
                            {
                                client.RequestSlot(member.Index, "FactionAmerica", startPosition: member.Index);
                                client.SetReady(true);
                            }
                            member.Seated = true;
                        }
                        if (client.IsHost && member.Seated && client.Room is { } full && full.Members.Length == members && full.Members.Count(other => other.Slot >= 0) == seats
                            && full.Members.All(other => other.Slot < 0 || other.Ready || other.IsHost) && (room.StartRequestedAt == long.MinValue || now - room.StartRequestedAt > 500_000))
                        {
                            room.StartRequestedAt = now;
                            client.RequestStart();
                        }
                        break;
                    case ClientPhase.Loading:
                        if (!member.Loaded)
                        {
                            client.ReportLoaded(LoadHash);
                            member.Loaded = true;
                        }
                        break;
                    case ClientPhase.InMatch:
                    {
                        if (member.MatchAtMicros < 0)
                        {
                            member.MatchAtMicros = now;
                            member.ReportedThrough = client.StartTick - 1;
                            if (member.Returning && client.StartTick > 0) Interlocked.Increment(ref totals.RejoinsBack);
                            if (member.Index == 0 && room.MatchAtMicros < 0)
                            {
                                room.MatchAtMicros = now;
                                Interlocked.Increment(ref totals.MatchesStarted);
                            }
                        }
                        if (client.RejoinToken != 0) member.Token = client.RejoinToken;
                        var interval = Math.Max(1, (int)client.Start!.HashInterval);
                        var played = 0;
                        while (member.ReportedThrough < client.NewestBundleTick)
                        {
                            var tick = ++member.ReportedThrough;
                            if (client.HasBundle(tick))
                            {
                                foreach (var command in client.TakeBundle(tick).Commands)
                                {
                                    if (command.Origin != client.ConnectionId || !member.SentAt.Remove(command.Receipt, out var sentAt)) continue;
                                    Interlocked.Increment(ref totals.OrdersSeen);
                                    lock (totals.LatencyMs)
                                    {
                                        if (totals.LatencyMs.Count < 2_000_000) totals.LatencyMs.Add((now - sentAt) / 1000.0);
                                    }
                                }
                            }
                            if ((tick - client.StartTick + 1) % interval == 0) client.ReportHash(tick, LoadHash);
                            client.ReportCompleted(tick);
                            played++;
                        }
                        Interlocked.Add(ref totals.TicksPlayed, played);
                        // Orders at the rate of a player who is busy: a move of a unit that does not exist - to the relay, bytes of the right kind and size.
                        if (member.Index < seats && client.Player > 0)
                        {
                            member.OrderDebt += played * ordersPerSecond / rate;
                            while (member.OrderDebt >= 1)
                            {
                                member.OrderDebt -= 1;
                                var player = new PlayerId(client.Player);
                                var receipt = member.NextReceipt++;
                                member.SentAt[receipt] = now;
                                if (member.SentAt.Count > 4_096) member.SentAt.Clear();
                                client.SendIntent(receipt, CommandWire.EncodeIntent((sequence, tick) => new SetMoveTargetCommand(player, sequence, tick, new EntityId((uint)(1 + receipt % 500)),
                                    Fixed.FromInt((int)(receipt % 3_000)), Fixed.FromInt((int)(receipt * 7 % 3_000)))));
                                Interlocked.Increment(ref totals.Orders);
                            }
                        }
                        if (!member.EndReported && member.ReportedThrough >= matchTicks - 1)
                        {
                            client.ReportEnd(matchTicks, 1);
                            member.EndReported = true;
                        }
                        if (room.Churn.Member == member.Index && member.ReportedThrough >= room.Churn.Tick)
                        {
                            room.Churn = (-1, -1, room.Churn.Kind, room.Churn.BackAfterMicros);
                            if (room.Churn.Kind == 1)
                            {
                                client.Leave();
                                member.Endpoint.Flush();
                                Interlocked.Increment(ref totals.Left);
                            }
                            else Interlocked.Increment(ref totals.Vanished);
                            // Vanished: the socket is gone without a word. Whether its seat is held is the server's configuration.
                            Account(member);
                            member.Dispose();
                            member.Gone = true;
                            member.BackAtMicros = room.Churn.Kind == 2 ? now + room.Churn.BackAfterMicros : -1;
                        }
                        break;
                    }
                }
                if (!member.Gone) member.Endpoint!.Flush();
            }
            // The end of a round: everybody who is still there was told the end. A room that does not get there is a finding.
            var present = room.Members.Where(member => member is { Gone: false, Client: not null }).ToArray();
            if (present.Length > 0 && present.All(member => member.Client!.End is not null || !member.Client.IsOpen))
            {
                var ended = present.FirstOrDefault(member => member.Client!.End is not null)?.Client!.End;
                Close(room, ended is { Reason: EndReason.Finished }, ended is null or { Reason: EndReason.Finished } ? null : $"ended with {ended.Reason} at tick {ended.Tick}");
                return;
            }
            var limit = room.MatchAtMicros < 0 ? 60_000_000 : (long)(matchSeconds * 2_500_000) + 90_000_000;
            if (now - (room.MatchAtMicros < 0 ? room.OpenedAtMicros : room.MatchAtMicros) > limit)
                Close(room, false, room.MatchAtMicros < 0 ? $"no match after 60 s (host in {host.Client?.Phase}; {host.Client?.LastRefusal?.Text})" : $"no end after {limit / 1_000_000} s (host at tick {host.ReportedThrough} of {matchTicks})");
            else if (!running && room.MatchAtMicros < 0) Close(room, false, null);
        }

        Console.WriteLine(Invariant($"load: {rooms} room(s) of {members} ({seats} seated, {members - seats} watching) against {server}, {seconds:F0} s, matches of {matchSeconds:F0} s at {rate} Hz, {ordersPerSecond} orders/s per player, churn {churnPercent} %, {threads} thread(s)"));
        var workers = Enumerable.Range(0, threads).Select(worker => new Thread(() =>
        {
            var mine = all.Where(room => room.Number % threads == worker).ToArray();
            while (true)
            {
                var now = clock.Microseconds;
                // After the time new rounds are not begun; the running ones play to their end (bounded by their own limit).
                var running = now < endAt;
                foreach (var room in mine) Step(room, now, running);
                if (!running && mine.All(room => room.Members.Length == 0 && room.Leaving.Length == 0)) break;
                Thread.Sleep(1);
            }
        }) { IsBackground = true }).ToArray();
        var watch = Stopwatch.StartNew();
        foreach (var worker in workers) worker.Start();
        var self = Process.GetCurrentProcess();
        var nextLine = 30_000L;
        while (workers.Any(worker => worker.IsAlive))
        {
            Thread.Sleep(200);
            if (watch.ElapsedMilliseconds < nextLine) continue;
            nextLine += 30_000;
            Console.WriteLine(Invariant($"load: {watch.Elapsed.TotalSeconds:F0} s - matches {Volatile.Read(ref totals.MatchesStarted)} started, {Volatile.Read(ref totals.MatchesFinished)} finished, {Volatile.Read(ref totals.Stuck)} stuck; ticks {Volatile.Read(ref totals.TicksPlayed)}; desyncs {Volatile.Read(ref totals.Desyncs)}; drops {Volatile.Read(ref totals.UnexpectedDrops)}"));
        }
        self.Refresh();
        double[] latency;
        lock (totals.LatencyMs) latency = [..totals.LatencyMs.Order()];
        double Quantile(double q) => latency.Length == 0 ? 0 : latency[Math.Min(latency.Length - 1, (int)(q * latency.Length))];
        var memberSeconds = Math.Max(1e-9, totals.MemberMicros / 1_000_000.0);
        var result = new
        {
            label, server = address, rooms, members, seats, seconds, matchSeconds, rate, ordersPerSecond, churnPercent, threads,
            elapsedSeconds = watch.Elapsed.TotalSeconds, totals.MatchesStarted, totals.MatchesFinished, totals.MatchesAbandoned, totals.Stuck, totals.Desyncs, totals.Refused, totals.UnexpectedDrops,
            totals.Discards, totals.TicksPlayed, totals.Orders, totals.OrdersSeen, totals.Vanished, totals.Left, totals.Rejoins, totals.RejoinsBack, totals.StatesOffered,
            // Per member and second of match: everything a member's socket sent and received, the handshake and the room included.
            bytesUpPerMemberSecond = totals.BytesSent / memberSeconds, bytesDownPerMemberSecond = totals.BytesReceived / memberSeconds,
            orderToBundleMsMedian = Quantile(0.5), orderToBundleMsP95 = Quantile(0.95), orderToBundleMsP99 = Quantile(0.99), orderToBundleMsMax = latency.Length == 0 ? 0 : latency[^1],
            generatorCpuSeconds = self.TotalProcessorTime.TotalSeconds, problems = totals.Problems.ToArray(),
        };
        if (Option(args, "--out") is { } file) File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(Invariant($"load: {result.MatchesFinished} of {result.MatchesStarted} match(es) finished, {result.MatchesAbandoned} abandoned at the end, {result.Stuck} stuck; {result.TicksPlayed} member ticks; orders {result.OrdersSeen} of {result.Orders} came back, median {result.orderToBundleMsMedian:F1} / 95 % {result.orderToBundleMsP95:F1} / 99 % {result.orderToBundleMsP99:F1} ms"));
        Console.WriteLine(Invariant($"load: vanished {result.Vanished}, left {result.Left}, came back {result.RejoinsBack} of {result.Rejoins} (states offered {result.StatesOffered}); desyncs {result.Desyncs}, refused {result.Refused}, unexpected drops {result.UnexpectedDrops}, discards {result.Discards}; {result.bytesUpPerMemberSecond:F0} B/s up and {result.bytesDownPerMemberSecond:F0} B/s down per member; generator CPU {result.generatorCpuSeconds:F1} s in {result.elapsedSeconds:F0} s"));
        foreach (var problem in totals.Problems.Take(20)) Console.WriteLine("load: problem: " + problem);
        var bad = result.Stuck + result.Desyncs + result.Refused + result.UnexpectedDrops;
        return bad > 0 || result.MatchesFinished == 0 ? 1 : 0;
    }
}

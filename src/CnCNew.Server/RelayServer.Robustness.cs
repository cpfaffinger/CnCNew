using System.Collections.Immutable;
using CnCNew.Net;

namespace CnCNew.Server;

// Phase 27 (docs/NETWORK-PROTOCOL.md section 18, docs/NETWORK-DIAGNOSTICS.md): what a running match needs when something goes wrong or somebody comes late - a seat that is
// held, a member that joins a running match (a player that comes back, a watcher), bundles on a watcher's delay, the parts of a desync, the rules of a pause.
public sealed partial class RelayServer
{
    private sealed record HeldSeat(ulong Token, long UntilMicros, int Slot, string Name);

    /// <summary>How far ahead of what a member has completed the server sends bundles: a member that plays nothing is not sent the whole match.</summary>
    private const int FeedWindowTicks = 512;
    private const long SnapshotWaitMicros = 10_000_000;
    private const long LoadWaitMicros = 180_000_000;

    private readonly SortedDictionary<int, HeldSeat> _held = [];
    private readonly Dictionary<long, ulong> _agreed = [];
    private readonly Queue<long> _agreedOrder = new();
    private readonly SortedDictionary<uint, DesyncDetailMessage> _details = [];
    private ulong _startHash;
    private uint _snapshotRequests;
    private long _delayTicks;
    private long _pauseUntilMicros;
    private long _endedAtMicros;
    private long _lastTickAtMicros;
    private ulong _tokens;
    private (long Tick, byte[] Bytes) _encodedBundle = (-1, []);

    /// <summary>Seats kept free for a player that lost its connection, by player number.</summary>
    public IReadOnlyCollection<int> HeldSeats => _held.Keys;

    /// <summary>Members that joined the running match (a player back in its seat, a watcher), and joins that were refused.</summary>
    public long LateJoins { get; private set; }
    public long LateJoinsRefused { get; private set; }
    public long SnapshotsRelayed { get; private set; }
    public long SnapshotsRefused { get; private set; }

    /// <summary>The parts of the session hash the clients differed in at the desync, once two of them have said theirs.</summary>
    public DesyncReportMessage? DesyncParts { get; private set; }

    /// <summary>The session hash all clients agreed on for a tick, as long as the server remembers it (the last ticks; longer for a delayed watcher).</summary>
    public bool TryGetAgreedHash(long tick, out ulong hash) => _agreed.TryGetValue(tick, out hash);

    private ulong NewToken()
    {
        // Only a server that holds seats or admits latecomers hands out tokens (and draws from its source for them).
        if (_options.SeedSource is not { } source || (_options.SeatHoldMs <= 0 && !_options.AllowLateWatchers)) return 0;
        // The count keeps two tokens of one room apart whatever the source draws.
        var token = (((ulong)source() << 32) | source()) ^ (++_tokens * 0x9E3779B97F4A7C15UL);
        return token == 0 ? 1 : token;
    }

    private void Agree(long tick, ulong hash)
    {
        if (tick > LastAgreedTick) LastAgreedTick = tick;
        _agreed[tick] = hash;
        _agreedOrder.Enqueue(tick);
        // Long enough for the report of a delayed watcher and for a snapshot that waits for its tick to be agreed.
        var keep = _delayTicks + 30L * Settings.TickRate + FeedWindowTicks;
        while (_agreedOrder.Count > keep) _agreed.Remove(_agreedOrder.Dequeue());
    }

    /// <summary>Who has to report a tick before it counts as agreed: the members that play along in time - not one that is still catching up, not a delayed watcher.</summary>
    private static bool ReportsInTime(Peer peer) => peer.Hello is not null && !peer.Joining && !peer.CatchingUp && !peer.Delayed;

    /// <summary>The newest tick a member may be sent: everything closed, minus the delay of a watcher.</summary>
    private long StreamHead(Peer peer)
    {
        if (!peer.Delayed) return NextTick - 1;
        // After the last tick - the end, or the match's full length - a delayed watcher is still fed at the pace of the match until it has seen all of it.
        var stopped = Phase == RoomPhase.Ended ? _endedAtMicros : NextTick >= Settings.MaxTicks ? _lastTickAtMicros : -1;
        var sinceStop = stopped < 0 ? 0 : (_clock.Microseconds - stopped) / Math.Max(1, _tickMicroseconds);
        return Math.Min(NextTick - 1, NextTick - 1 - _delayTicks + sinceStop);
    }

    private byte[] EncodedBundle(long tick)
    {
        if (_encodedBundle.Tick != tick) _encodedBundle = (tick, MessageCodec.Encode(Recording.Bundles[(int)(tick - Recording.StartTick)]));
        return _encodedBundle.Bytes;
    }

    /// <summary>Sends a member the bundles it may have and has not got yet.</summary>
    private void Feed(Peer peer)
    {
        if (peer.Hello is null || peer.Joining || !peer.Connection.IsOpen) return;
        var limit = Math.Min(StreamHead(peer), peer.CompletedTick + FeedWindowTicks);
        // The urgent channel: a bundle must not wait behind a lost packet.
        for (; peer.NextBundle <= limit; peer.NextBundle++) peer.Connection.SendUrgent(EncodedBundle(peer.NextBundle));
        if (peer.PendingEnd is { } end && peer.NextBundle >= NextTick)
        {
            peer.PendingEnd = null;
            Send(peer, end);
        }
    }

    private void FeedAll()
    {
        // The newest bundle first for those that are up to date (it is encoded once), then whoever has a backlog.
        foreach (var peer in _peers) Feed(peer);
    }

    // ------------------------------------------------------------------------------------------------------ held seats

    private void HoldSeat(Peer peer)
    {
        var until = peer.SeatUntilMicros != 0 ? peer.SeatUntilMicros : _clock.Microseconds + _options.SeatHoldMs * 1000L;
        _held[peer.Player] = new HeldSeat(peer.Token, until, peer.Slot, peer.Hello!.Name);
        var left = (int)Math.Max(0, (until - _clock.Microseconds) / 1000);
        Log("seat-held", ("player", Text(peer.Player)), ("name", peer.Hello.Name), ("holdMs", Text(left)), ("tick", Text(NextTick)));
        Broadcast(new SeatStateMessage(peer.Player, SeatHold.Held, left));
    }

    private void ExpireSeats(long now)
    {
        if (_held.Count == 0) return;
        foreach (var (player, seat) in _held.Where(pair => pair.Value.UntilMicros <= now).ToList())
        {
            _held.Remove(player);
            // Nobody came back: now the player leaves the match, by a command of the next bundle, as a lost connection does without a held seat.
            _leaves.Add((player, false));
            Log("seat-given-up", ("player", Text(player)), ("name", seat.Name), ("tick", Text(NextTick)));
            Broadcast(new SeatStateMessage(player, SeatHold.GivenUp, 0));
        }
    }

    // ------------------------------------------------------------------------------------------- joining a running match

    /// <summary>A hello while the match runs: a player that presents the token of a held seat, or somebody who wants to watch. Returns the rejection, or null when admitted.</summary>
    private RejectMessage? AdmitLate(Peer peer, HelloMessage hello)
    {
        if (hello.RejoinToken != 0)
        {
            var seat = _held.FirstOrDefault(pair => pair.Value.Token == hello.RejoinToken);
            if (seat.Value is null) return new RejectMessage(RejectReason.SeatNotHeld, "No seat is held for this client: it was never in this match, or its time is over.");
            _held.Remove(seat.Key);
            peer.Player = seat.Key;
            peer.Slot = seat.Value.Slot;
            peer.Token = seat.Value.Token;
            peer.SeatUntilMicros = seat.Value.UntilMicros;
            Log("seat-returned", ("player", Text(peer.Player)), ("connection", Text(peer.Id)), ("name", hello.Name), ("tick", Text(NextTick)));
        }
        else
        {
            if (!_options.AllowLateWatchers) return new RejectMessage(RejectReason.MatchRunning, "The match has started, and this server admits nobody to a running match.");
            peer.Player = 0;
            peer.Slot = -1;
            peer.Token = NewToken();
        }
        peer.Hello = hello;
        peer.Joining = true;
        peer.Delayed = peer.Player == 0 && _delayTicks > 0;
        peer.JoinDeadlineMicros = _clock.Microseconds + LoadWaitMicros;
        Send(peer, new WelcomeMessage(peer.Id, false, peer.Token));
        if (peer.Player > 0) Broadcast(new SeatStateMessage(peer.Player, SeatHold.Returned, 0));
        BroadcastRoom();
        // A watcher on a delay must not be handed the present: it plays the match again from its start. Everybody else takes over the state of a member.
        if (peer.Delayed) StartFromBeginning(peer);
        else RequestSnapshot(peer);
        return null;
    }

    private void RequestSnapshot(Peer peer)
    {
        var donor = _peers.FirstOrDefault(other => other != peer && ReportsInTime(other) && other.Connection.IsOpen && !peer.TriedDonors.Contains(other.Id));
        if (donor is null)
        {
            StartFromBeginning(peer);
            return;
        }
        peer.TriedDonors.Add(donor.Id);
        peer.SnapshotRequest = ++_snapshotRequests;
        peer.SnapshotDonor = donor.Id;
        peer.SnapshotDeadlineMicros = _clock.Microseconds + SnapshotWaitMicros;
        peer.Offer = null;
        Send(donor, new SnapshotRequestMessage(peer.SnapshotRequest));
    }

    /// <summary>No member can hand over its state (or must not): the joiner builds the match as it began and is sent every bundle since.</summary>
    private void StartFromBeginning(Peer peer)
    {
        peer.SnapshotRequest = 0;
        if (NextTick - Recording.StartTick > _options.CatchUpLimitTicks)
        {
            RefuseJoin(peer, new RejectMessage(RejectReason.CannotCatchUp, "The match is too long to be played again from its start, and no member could hand over its state."));
            return;
        }
        StartLate(peer, Recording.Resume, _startHash);
    }

    private void StartLate(Peer peer, ResumeState? resume, ulong expectedHash)
    {
        peer.SnapshotRequest = 0;
        peer.Offer = null;
        peer.ExpectedHash = expectedHash;
        peer.NextBundle = resume?.Tick ?? Recording.StartTick;
        peer.CompletedTick = peer.NextBundle - 1;
        peer.Loaded = false;
        peer.EndReport = null;
        peer.Intents.Clear();
        peer.JoinDeadlineMicros = _clock.Microseconds + LoadWaitMicros;
        Send(peer, new MatchStartMessage(Recording.Description, peer.Player, Pacing.Server, _options.HashInterval, resume));
    }

    private void RefuseJoin(Peer peer, RejectMessage reject)
    {
        LateJoinsRefused++;
        Log("join-refused", ("connection", Text(peer.Id)), ("player", Text(peer.Player)), ("reason", reject.Reason.ToString()), ("text", reject.Text));
        Send(peer, reject);
        peer.Connection.Close();
        Drop(peer, DropReason.ProtocolViolation);
    }

    private void TakeSnapshot(Peer donor, SnapshotOfferMessage offer)
    {
        var peer = _peers.FirstOrDefault(other => other.Joining && other.SnapshotRequest == offer.RequestId && other.SnapshotDonor == donor.Id);
        if (peer is null || offer.RequestId == 0)
        {
            Discard(donor, $"a snapshot nobody asked for (request {offer.RequestId})");
            return;
        }
        if (offer.State.Tick <= Recording.StartTick || offer.State.Tick > NextTick)
        {
            Discard(donor, $"a snapshot of tick {offer.State.Tick} while tick {NextTick} is next");
            SnapshotsRefused++;
            RequestSnapshot(peer);
            return;
        }
        peer.Offer = offer.State;
    }

    /// <summary>
    /// A snapshot is passed on once every client has reported the tick before it and its hash is the one they agreed on: the server does not simulate, but it knows what
    /// the state must hash to. A snapshot with another hash - damaged, manipulated, or of a client that has diverged - goes nowhere.
    /// </summary>
    private void DeliverSnapshots(long now)
    {
        for (var index = _peers.Count - 1; index >= 0; index--)
        {
            var peer = _peers[index];
            if (!peer.Joining) continue;
            if (peer.Offer is { } offer)
            {
                var tick = offer.Tick - 1;
                if (_agreed.TryGetValue(tick, out var agreed))
                {
                    if (agreed == offer.SessionHash)
                    {
                        SnapshotsRelayed++;
                        Log("snapshot-relayed", ("to", Text(peer.Id)), ("from", Text(peer.SnapshotDonor)), ("tick", Text(offer.Tick)), ("bytes", Text(offer.State.Length)));
                        StartLate(peer, offer, offer.SessionHash);
                    }
                    else
                    {
                        SnapshotsRefused++;
                        Log("snapshot-refused", ("from", Text(peer.SnapshotDonor)), ("tick", Text(offer.Tick)), ("hash", offer.SessionHash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)),
                            ("agreed", agreed.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)));
                        RequestSnapshot(peer);
                    }
                    continue;
                }
                // Agreed long ago and forgotten: of no use any more.
                if (tick <= LastAgreedTick)
                {
                    SnapshotsRefused++;
                    RequestSnapshot(peer);
                    continue;
                }
            }
            if (peer.SnapshotRequest != 0 && now >= peer.SnapshotDeadlineMicros)
            {
                SnapshotsRefused++;
                Log("snapshot-late", ("from", Text(peer.SnapshotDonor)), ("to", Text(peer.Id)));
                RequestSnapshot(peer);
            }
            else if (peer.SnapshotRequest == 0 && now >= peer.JoinDeadlineMicros)
            {
                RefuseJoin(peer, new RejectMessage(RejectReason.CannotCatchUp, "The match was not built in time."));
            }
        }
    }

    /// <summary>The joiner has built the match: its state must hash to what the server expects - the agreed hash of the snapshot's tick, or the hash the match began with.</summary>
    private void LateLoaded(Peer peer, LoadedMessage loaded)
    {
        if (loaded.SessionHash != peer.ExpectedHash)
        {
            RefuseJoin(peer, new RejectMessage(RejectReason.CannotCatchUp, "The state this client built does not hash to the state of the match."));
            return;
        }
        peer.Joining = false;
        peer.CatchingUp = true;
        peer.Loaded = true;
        peer.SeatUntilMicros = 0;
        peer.LagFloorNow = int.MaxValue;
        peer.LagFloorBefore = int.MaxValue;
        peer.LagWindowStartMicros = _clock.Microseconds;
        peer.ThrottledMicros = 0;
        LateJoins++;
        Log("joined-late", ("connection", Text(peer.Id)), ("player", Text(peer.Player)), ("name", peer.Hello!.Name), ("fromTick", Text(peer.NextBundle)), ("tick", Text(NextTick)),
            ("delayed", peer.Delayed ? "true" : "false"));
        Send(peer, new AllLoadedMessage(peer.NextBundle));
        Feed(peer);
    }

    // ------------------------------------------------------------------------------------------------------------ desync

    private void TakeDetail(Peer peer, DesyncDetailMessage detail)
    {
        if (Desync is null || detail.Tick != Desync.Tick)
        {
            Discard(peer, $"a desync detail for tick {detail.Tick}");
            return;
        }
        _details[peer.Id] = detail;
        if (_details.Values.Count(value => !value.Parts.IsEmpty) < 2) return;
        var parts = ImmutableArray.CreateBuilder<DesyncPart>();
        foreach (var name in _details.Values.SelectMany(value => value.Parts).Select(part => part.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var values = _details.Where(pair => !pair.Value.Parts.IsEmpty).Select(pair => (pair.Key, Part: pair.Value.Parts.FirstOrDefault(part => part.Name == name))).ToList();
            // A part is named when the clients that sent their parts do not all have it with one value (one that lacks it has another state too).
            if (values.All(value => value.Part.Name == name) && values.Select(value => value.Part.Hash).Distinct().Count() == 1) continue;
            foreach (var (connection, part) in values) parts.Add(new DesyncPart(name, connection, part.Name == name ? part.Hash : 0));
        }
        DesyncParts = new DesyncReportMessage(detail.Tick, parts.ToImmutable());
        Log("desync-parts", ("tick", Text(detail.Tick)), ("clients", Text(_details.Count)),
            ("parts", string.Join("; ", DesyncParts.Parts.GroupBy(part => part.Name).Select(group => group.Key + " " + string.Join(' ', group.Select(part => $"{part.ConnectionId}:{part.Hash:X16}"))))));
        Broadcast(DesyncParts);
    }

    // ------------------------------------------------------------------------------------------------------------- pause

    /// <summary>
    /// The pace of a server-paced match (docs/NETWORK-PROTOCOL.md section 18.5): only players ask; a pause is limited in number per player and in length, anybody who
    /// plays may end it; the speed is the host's to set. Returns the refusal, or null when the request stands.
    /// </summary>
    private RejectMessage? CheckPace(Peer peer, PaceRequestMessage pace)
    {
        if (Pacing != Pacing.Server) return null;
        if (peer.Player == 0) return new RejectMessage(RejectReason.InvalidRequest, "Only players set the pace of a match.");
        if (pace.SpeedPercent != SpeedPercent && _options.HostSetsSpeed && peer.Id != _host) return new RejectMessage(RejectReason.NotHost, "Only the host sets the speed.");
        if (pace.Paused && !Paused)
        {
            if (peer.PausesUsed >= _options.PausesPerPlayer) return new RejectMessage(RejectReason.InvalidRequest, $"This player has used its {_options.PausesPerPlayer} pauses.");
            peer.PausesUsed++;
            _pauseUntilMicros = _options.MaxPauseMs > 0 ? _clock.Microseconds + _options.MaxPauseMs * 1000L : 0;
            Log("paused", ("by", Text(peer.Id)), ("player", Text(peer.Player)), ("tick", Text(NextTick)), ("pausesLeft", Text(_options.PausesPerPlayer - peer.PausesUsed)));
        }
        else if (!pace.Paused && Paused)
        {
            Log("resumed", ("by", Text(peer.Id)), ("tick", Text(NextTick)));
        }
        return null;
    }

    private void EndOverlongPause(long now)
    {
        if (!Paused || _pauseUntilMicros == 0 || now < _pauseUntilMicros) return;
        Paused = false;
        _pauseUntilMicros = 0;
        Log("resumed", ("by", "0"), ("tick", Text(NextTick)), ("reason", "the pause reached its limit"));
        Broadcast(new PaceStateMessage(false, (ushort)Math.Max(1, SpeedPercent * (Throttling ? _options.ThrottleSpeedPercent : 100) / 100), 0));
    }
}

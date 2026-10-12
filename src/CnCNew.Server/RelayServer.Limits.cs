using CnCNew.Net;

namespace CnCNew.Server;

// Phase 28 (docs/NETWORK-PROTOCOL.md sections 20.4 and 20.5, docs/DEDICATED-SERVER.md section 9): what a member may cost a room, and who may be in it. Every limit is
// an option with a documented default; every violation is counted and has one defined reaction - a message discarded, a request refused, a member dropped, a match
// ended - and none of them lets the room's memory or work grow without a bound.
public sealed partial class RelayServer
{
    private const long TokenScale = 1_000_000;

    private readonly HashSet<string> _kicked = new(StringComparer.Ordinal);
    private string _verifier = string.Empty;
    private ulong _nonces;
    private long _loadingSinceMicros;
    private long _phaseSinceMicros;
    private RoomPhase _phaseSeen = RoomPhase.Room;
    private bool _idleClosed;
    private long _snapshotWindowMicros = long.MinValue;
    private int _snapshotsInWindow;

    /// <summary>Whether the room has a password (docs/NETWORK-PROTOCOL.md section 20.5).</summary>
    public bool HasPassword => _verifier.Length > 0;

    /// <summary>Wrong answers to the password challenge, members the host removed, and origins that may not come back.</summary>
    public long BadPasswords { get; private set; }
    public long Kicks { get; private set; }
    public int KickedOrigins => _kicked.Count;

    /// <summary>Members dropped because they sent more messages than a connection may.</summary>
    public long FloodDrops { get; private set; }
    /// <summary>Members dropped because they did not report the hash of a tick in time, and members dropped because they did not finish loading in time.</summary>
    public long HashStallDrops { get; private set; }
    public long LoadTimeouts { get; private set; }
    /// <summary>Connections closed because they neither said a hello nor answered the password challenge in time.</summary>
    public long HelloTimeouts { get; private set; }
    /// <summary>Session states refused because they were larger than the room takes, and joins refused because too many asked for a state within a minute.</summary>
    public long StatesRefusedForSize { get; private set; }
    public long JoinsRateLimited { get; private set; }
    /// <summary>What the commands of the match add up to in the recording, in bytes (docs/DEDICATED-SERVER.md section 9).</summary>
    public long RecordingBytes { get; private set; }

    /// <summary>One message of a member against its allowance: refilled by the clock at the long-run rate, up to the burst. False: the member sends too much.</summary>
    private bool MessageAllowed(Peer peer)
    {
        // The one client of a local game closes its own ticks, as fast as it likes: there is nobody to protect from it.
        if (_options.MessagesPerSecond <= 0 || (Pacing == Pacing.Client && Phase != RoomPhase.Room)) return true;
        var now = _clock.Microseconds;
        peer.MessageTokens = Math.Min(_options.MessageBurst * TokenScale, peer.MessageTokens + Math.Max(0, now - peer.MessageAtMicros) * _options.MessagesPerSecond);
        peer.MessageAtMicros = now;
        if (peer.MessageTokens < TokenScale) return false;
        peer.MessageTokens -= TokenScale;
        return true;
    }

    private void EnforceLimits(long now)
    {
        if (Phase != _phaseSeen)
        {
            _phaseSeen = Phase;
            _phaseSinceMicros = now;
        }
        // A connection that says nothing - no hello, no answer to the challenge - holds a place for nobody.
        if (_options.HelloLimitMs > 0)
            for (var index = _peers.Count - 1; index >= 0; index--)
            {
                var peer = _peers[index];
                if (peer.Hello is not null || now - peer.AttachedMicros < _options.HelloLimitMs * 1000L) continue;
                HelloTimeouts++;
                Log("no-hello", ("connection", Text(peer.Id)), ("challenged", peer.PendingHello is null ? "false" : "true"));
                peer.Connection.Close();
                _peers.RemoveAt(index);
            }
        // A member that never finishes loading must not keep the others in front of their loading screens.
        if (Phase == RoomPhase.Loading && _options.LoadLimitMs > 0 && now - _loadingSinceMicros >= _options.LoadLimitMs * 1000L)
            foreach (var peer in _peers.Where(peer => peer.Hello is not null && !peer.Loaded).ToList())
            {
                if (Phase != RoomPhase.Loading) break;
                LoadTimeouts++;
                Log("load-timeout", ("connection", Text(peer.Id)), ("player", Text(peer.Player)));
                peer.Connection.Close();
                Drop(peer, DropReason.TooSlow);
            }
        // A lobby that never starts, and a match that is over: after a long while everybody is sent home and the room is free again.
        if (!_idleClosed && _options.RoomIdleLimitMs > 0 && Phase is RoomPhase.Room or RoomPhase.Ended && _peers.Count > 0 && now - _phaseSinceMicros >= _options.RoomIdleLimitMs * 1000L)
        {
            _idleClosed = true;
            Log("room-idle", ("phase", Phase.ToString()), ("connections", Text(_peers.Count)));
            foreach (var peer in _peers) peer.Connection.Close();
        }
    }

    /// <summary>
    /// The hash of a tick may lag as far as the slowest member that still plays - the rule for members that fall behind bounds that. A member that plays along and
    /// does not report is something else: the others' reports wait for it, nothing is agreed on, and nobody could join. It is dropped.
    /// </summary>
    private void CheckHashStall()
    {
        if (_options.HashReportLimitMs <= 0) return;
        var interval = Math.Max(1, (int)_options.HashInterval);
        var after = Math.Max(LastAgreedTick, Recording.StartTick - 1) + 1;
        var remainder = (after - Recording.StartTick + 1) % interval;
        var tick = after + (remainder == 0 ? 0 : interval - remainder);
        if ((NextTick - 1 - tick) * _tickMicroseconds < _options.HashReportLimitMs * 1000L) return;
        _hashReports.TryGetValue(tick, out var reports);
        foreach (var peer in _peers.Where(peer => Decides(peer) && (reports is null || !reports.ContainsKey(peer.Id))).ToList())
        {
            if (Phase != RoomPhase.Match) break;
            HashStallDrops++;
            Log("hash-stalled", ("connection", Text(peer.Id)), ("player", Text(peer.Player)), ("tick", Text(tick)), ("serverTick", Text(NextTick)));
            peer.Connection.Close();
            Drop(peer, DropReason.ProtocolViolation);
        }
    }

    /// <summary>Whether a member may be asked for its state now: a few times a minute at most - every request stops one player's game for the time it takes to write it.</summary>
    private bool SnapshotAllowed()
    {
        if (_options.SnapshotsPerMinute <= 0) return true;
        var now = _clock.Microseconds;
        if (_snapshotWindowMicros == long.MinValue || now - _snapshotWindowMicros >= 60_000_000)
        {
            _snapshotWindowMicros = now;
            _snapshotsInWindow = 0;
        }
        if (_snapshotsInWindow >= _options.SnapshotsPerMinute)
        {
            JoinsRateLimited++;
            return false;
        }
        _snapshotsInWindow++;
        return true;
    }

    private bool TokenIsKnown(ulong token) => _held.Values.Any(seat => seat.Token == token) || _peers.Any(peer => peer.Token == token && peer.Player > 0 && peer.Hello is not null);

    private string NewNonce()
    {
        // Never twice in the life of a room, whatever the source draws; unpredictable where the server has a real source.
        Span<byte> bytes = stackalloc byte[16];
        var counter = ++_nonces;
        for (var index = 0; index < 4; index++)
        {
            var word = (_options.SeedSource?.Invoke() ?? 0) ^ unchecked((uint)(counter * 0x9E3779B97F4A7C15UL / 4_294_967_296UL)) ^ (uint)index * 0x85EBCA6BU;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes[(index * 4)..], word);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes) ^ counter);
        return Convert.ToHexString(bytes);
    }

    private void TakePassword(Peer peer, HelloMessage hello, PasswordResponseMessage response)
    {
        peer.PendingHello = null;
        if (RoomPassword.Matches(_verifier, peer.Nonce, response.Answer))
        {
            peer.PasswordPassed = true;
            Admit(peer, hello);
            return;
        }
        BadPasswords++;
        Log("bad-password", ("connection", Text(peer.Id)));
        Send(peer, new RejectMessage(RejectReason.BadPassword, "The password of the room is another one."));
        peer.Connection.Close();
        _peers.Remove(peer);
    }

    private void Kick(Peer host, uint connectionId)
    {
        if (host.Id != _host)
        {
            Refuse(host, RejectReason.NotHost, "Only the host removes a member.");
            return;
        }
        var target = _peers.FirstOrDefault(peer => peer.Id == connectionId && peer.Hello is not null);
        if (target is null || target == host)
        {
            Refuse(host, RejectReason.InvalidRequest, "There is no such member to remove.");
            return;
        }
        Kicks++;
        if (target.Origin.Length > 0) _kicked.Add(target.Origin);
        Log("kicked", ("connection", Text(target.Id)), ("player", Text(target.Player)), ("by", Text(host.Id)), ("phase", Phase.ToString()));
        Send(target, new RejectMessage(RejectReason.Kicked, "The host of the room removed you."));
        target.Connection.Close();
        Drop(target, DropReason.Kicked);
    }
}

using CnCNew.Net;

namespace CnCNew.Server;

/// <summary>
/// The votes of a running match (D-23, docs/NETWORK-PROTOCOL.md section 24): the players may remove a player, end the match or set the speed. One vote runs at a time, whoever
/// starts one has said yes, and a strict majority of those who may vote decides; time decides against it. A vote costs the server a few bytes, and a player has
/// <see cref="RelayOptions.VotesPerPlayer"/> of them.
/// </summary>
public sealed partial class RelayServer
{
    private sealed record Vote(VoteKind Kind, uint Target, uint Starter, long UntilMicros)
    {
        public SortedDictionary<uint, bool> Ballots { get; } = [];
    }

    private Vote? _vote;

    /// <summary>Votes started, and those that passed.</summary>
    public long VotesStarted { get; private set; }
    public long VotesPassed { get; private set; }

    /// <summary>Who may vote: the players that play along in time, without the one the vote is about.</summary>
    private List<Peer> Voters(VoteKind kind, uint target)
        => [.._peers.Where(peer => peer.Player > 0 && ReportsInTime(peer) && !(kind == VoteKind.RemovePlayer && peer.Id == target))];

    private void StartVote(Peer peer, VoteRequestMessage request)
    {
        if (_options.VotesPerPlayer <= 0 || Pacing != Pacing.Server) { Refuse(peer, RejectReason.InvalidRequest, "This match has no votes."); return; }
        if (peer.Player == 0 || !ReportsInTime(peer)) { Refuse(peer, RejectReason.InvalidRequest, "Only players vote."); return; }
        if (_vote is not null) { Refuse(peer, RejectReason.InvalidRequest, "A vote is running."); return; }
        if (peer.VotesStarted >= _options.VotesPerPlayer) { Refuse(peer, RejectReason.InvalidRequest, $"This player has used its {_options.VotesPerPlayer} votes."); return; }
        switch (request.Kind)
        {
            case VoteKind.RemovePlayer when !_peers.Any(other => other.Id == request.Target && other.Player > 0 && other != peer):
                Refuse(peer, RejectReason.InvalidRequest, "There is no such player to remove.");
                return;
            case VoteKind.RemovePlayer when Voters(request.Kind, request.Target).Count < 2:
                Refuse(peer, RejectReason.InvalidRequest, "It takes two others to remove a player.");
                return;
            case VoteKind.Speed when request.Target is < Protocol.MinSpeedPercent or > Protocol.MaxSpeedPercent:
                Discard(peer, $"a vote for a speed of {request.Target} percent");
                return;
        }
        peer.VotesStarted++;
        VotesStarted++;
        _vote = new Vote(request.Kind, request.Target, peer.Id, _clock.Microseconds + _options.VoteTimeMs * 1000L);
        _vote.Ballots[peer.Id] = true;
        Log("vote", ("kind", request.Kind.ToString()), ("target", Text(request.Target)), ("by", Text(peer.Id)));
        TallyVote();
    }

    private void CastVote(Peer peer, bool yes)
    {
        if (_vote is null || peer.Player == 0 || !Voters(_vote.Kind, _vote.Target).Contains(peer) || !_vote.Ballots.TryAdd(peer.Id, yes)) { Discard(peer, "a ballot nobody asked for"); return; }
        TallyVote();
    }

    private void TallyVote()
    {
        var vote = _vote!;
        var voters = Voters(vote.Kind, vote.Target);
        var needed = voters.Count / 2 + 1;
        var yes = voters.Count(voter => vote.Ballots.GetValueOrDefault(voter.Id));
        var no = voters.Count(voter => vote.Ballots.TryGetValue(voter.Id, out var ballot) && !ballot);
        var result = yes >= needed ? VoteResult.Passed : no > voters.Count - needed || _clock.Microseconds >= vote.UntilMicros ? VoteResult.Failed : VoteResult.Running;
        Broadcast(new VoteStateMessage(vote.Kind, vote.Target, vote.Starter, (byte)yes, (byte)no, (byte)voters.Count, result));
        if (result == VoteResult.Running) return;
        _vote = null;
        Log("vote-ended", ("kind", vote.Kind.ToString()), ("target", Text(vote.Target)), ("result", result.ToString()), ("yes", Text(yes)), ("no", Text(no)));
        if (result != VoteResult.Passed) return;
        VotesPassed++;
        switch (vote.Kind)
        {
            case VoteKind.RemovePlayer:
                if (_peers.FirstOrDefault(peer => peer.Id == vote.Target) is { } removed)
                {
                    Send(removed, new RejectMessage(RejectReason.VotedOut, "The players removed you from the match."));
                    removed.Connection.Close();
                    Drop(removed, DropReason.VotedOut);
                }
                break;
            case VoteKind.EndMatch:
                Finish(new MatchEndMessage(NextTick - 1, 0, EndReason.Voted));
                break;
            case VoteKind.Speed:
                SpeedPercent = (int)vote.Target;
                Broadcast(new PaceStateMessage(Paused, (ushort)Math.Max(1, SpeedPercent * (Throttling ? _options.ThrottleSpeedPercent : 100) / 100), 0));
                break;
        }
    }

    /// <summary>Time decides a vote as ballots do.</summary>
    private void CheckVote(long now)
    {
        if (_vote is { } vote && now >= vote.UntilMicros) TallyVote();
    }
}

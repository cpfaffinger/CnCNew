using CnCNew.Net;
using CnCNew.Server;

// Phase 53: the votes of a match (D-23, docs/NETWORK-PROTOCOL.md section 24) and the majority over a hash (D-24).
static partial class NetTests
{
    private static RoomSettings Seats(int players) => TwoSeats() with
    {
        Slots = [..Enumerable.Range(0, players).Select(index => new CnCNew.Simulation.Skirmish.MatchSlotDescription(CnCNew.Simulation.Skirmish.MatchSlotKind.Human, index == 0 ? "Alpha" : "Beta", StartPosition: index))],
    };

    public static void HashMajority()
    {
        // ---- Four players: the one who differs goes, and the match goes on with the hash of the others.
        var net = new Net();
        net.StartMatch(4, Seats(4));
        net.Pump(10, 20);
        foreach (var client in net.Clients) client.ReportHash(0, 100);
        net.Pump(1);
        Check.That(net.Server.LastAgreedTick == 0, "equal reports agree");
        net.Clients[0].ReportHash(1, 101);
        net.Clients[1].ReportHash(1, 101);
        net.Clients[2].ReportHash(1, 999);
        net.Pump(1);
        Check.That(net.Server.Phase == RoomPhase.Match && net.Server.Desync is null && net.Clients[2].IsOpen, "a difference waits for the last report");
        net.Clients[3].ReportHash(1, 101);
        net.Pump(2);
        Check.That(net.Server.Phase == RoomPhase.Match && net.Server.PlayersOutvoted == 1 && net.Server.Desync is null, $"the majority decides ({net.Server.Phase}, {net.Server.PlayersOutvoted})");
        Check.That(net.Server.LastAgreedTick == 1, "and the tick is agreed on its hash");
        Check.That(net.Clients[2].Desync is { Tick: 1 } && !net.Clients[2].IsOpen, "the one who differs is told, and goes");
        Check.That(net.Clients[0].Dropped.Any(drop => drop.Reason == DropReason.Desync) && net.Clients[0].Desync is null, "the others are told it was dropped, and have no desync");
        net.Pump(30, 20);
        Check.That(net.Server.Recording.Bundles.SelectMany(bundle => bundle.Commands).Count(command => command.Origin == 0 && command.Player == 3) == 1, "its player leaves the match by a command");

        // ---- Without a majority nobody knows who is right: the match ends as before.
        var tied = new Net();
        tied.StartMatch(4, Seats(4));
        tied.Pump(10, 20);
        tied.Clients[0].ReportHash(0, 1);
        tied.Clients[1].ReportHash(0, 1);
        tied.Clients[2].ReportHash(0, 2);
        tied.Clients[3].ReportHash(0, 2);
        tied.Pump(2);
        Check.That(tied.Server.Phase == RoomPhase.Ended && tied.Server.Desync is not null && tied.Server.PlayersOutvoted == 0, "two against two is a desync of the match");

        // ---- Two players are no majority.
        var pair = new Net();
        pair.StartMatch(2, Seats(2));
        pair.Pump(10, 20);
        pair.Clients[0].ReportHash(0, 1);
        pair.Clients[1].ReportHash(0, 2);
        pair.Pump(2);
        Check.That(pair.Server.Phase == RoomPhase.Ended && pair.Server.Desync is not null, "two players who differ end the match");
    }

    public static void Votes()
    {
        Net Room(int players, RelayOptions? options = null)
        {
            var net = new Net(options ?? Bare() with { HashReportLimitMs = 0 });
            net.StartMatch(players, Seats(players));
            net.Pump(10, 20);
            return net;
        }

        // ---- Removing a player: a strict majority of the others.
        var net = Room(4);
        var target = net.Clients[3];
        var targetId = net.Clients[3].ConnectionId;
        net.Clients[0].RequestVote(VoteKind.RemovePlayer, targetId);
        net.Pump(1);
        Check.That(net.Clients[1].Vote is { Kind: VoteKind.RemovePlayer, Yes: 1, Voters: 3, Result: VoteResult.Running } && net.Server.VotesStarted == 1, "a vote starts with the yes of its starter, and everybody sees it");
        net.Clients[1].RequestVote(VoteKind.EndMatch);
        net.Pump(1);
        Check.That(net.Clients[1].LastRefusal is { Reason: RejectReason.InvalidRequest } && net.Server.VotesStarted == 1, "one vote at a time");
        target.CastVote(false);
        net.Pump(1);
        Check.That(net.Server.Discarded > 0 && net.Clients[0].Vote is { No: 0 }, "the one a vote is about has no ballot");
        net.Clients[1].CastVote(true);
        net.Pump(6);
        Check.That(net.Server.VotesPassed == 1 && !target.IsOpen && target.LastRefusal is { Reason: RejectReason.VotedOut } && net.Clients[0].Dropped.Any(drop => drop.Reason == DropReason.VotedOut),
            $"two of three remove the player, who is told ({net.Server.VotesPassed}, {target.IsOpen}, {target.LastRefusal}, {string.Join(",", net.Clients[0].Dropped.Select(d => d.Reason))})");
        Check.That(net.Clients[0].Vote is null && net.Clients[0].VoteEvents[^1].Result == VoteResult.Passed, "and everybody sees the result");

        // ---- Two players cannot remove each other; a watcher has no vote.
        var pair = Room(2);
        pair.Clients[0].RequestVote(VoteKind.RemovePlayer, pair.Clients[1].ConnectionId);
        pair.Pump(1);
        Check.That(pair.Server.VotesStarted == 0 && pair.Clients[0].LastRefusal is not null, "it takes two others to remove a player");

        // ---- The end of a match, the speed.
        var ended = Room(3);
        ended.Clients[0].RequestVote(VoteKind.EndMatch);
        ended.Pump(1);
        ended.Clients[1].CastVote(true);
        ended.Pump(2);
        Check.That(ended.Server.Phase == RoomPhase.Ended && ended.Server.End is { Reason: EndReason.Voted }, "two of three end the match");

        var speed = Room(3);
        speed.Clients[1].RequestVote(VoteKind.Speed, 150);
        speed.Pump(1);
        speed.Clients[2].CastVote(true);
        speed.Pump(2);
        Check.That(speed.Server.SpeedPercent == 150, $"a vote sets the speed for those who are not the host ({speed.Server.SpeedPercent})");

        // ---- Refused by the others, by time, and bounded.
        var refused = Room(3);
        refused.Clients[0].RequestVote(VoteKind.EndMatch);
        refused.Pump(1);
        refused.Clients[1].CastVote(false);
        refused.Clients[2].CastVote(false);
        refused.Pump(2);
        Check.That(refused.Server.Phase == RoomPhase.Match && refused.Clients[0].VoteEvents[^1].Result == VoteResult.Failed, "two no fail it");

        var timed = Room(3);
        timed.Clients[0].RequestVote(VoteKind.EndMatch);
        timed.Pump(1);
        timed.Pump(100, 400);
        Check.That(timed.Server.Phase == RoomPhase.Match && timed.Clients[1].Vote is null && timed.Clients[1].VoteEvents[^1].Result == VoteResult.Failed, "a vote nobody answers ends by time");

        var bounded = Room(3, Bare() with { HashReportLimitMs = 0, VotesPerPlayer = 2, VoteTimeMs = 1_000 });
        for (var round = 0; round < 3; round++)
        {
            bounded.Clients[0].RequestVote(VoteKind.EndMatch);
            bounded.Pump(30, 100);
        }
        Check.That(bounded.Server.VotesStarted == 2, $"a player has its votes ({bounded.Server.VotesStarted})");
    }
}

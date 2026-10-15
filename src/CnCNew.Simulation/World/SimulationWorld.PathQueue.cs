using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

/// <summary>
/// The path queue of the movement profile <see cref="MovementProfile.Budgeted"/> (Stage 3, S3-M6; docs/PERF-MOVEMENT.md section 9). Under the original profile it is never used and
/// nothing of it is written.
/// <para>
/// A ground unit that needs a path asks the world (<see cref="RequestPath"/>) instead of searching itself, and takes the answer in a later update (<see cref="TryTakePath"/>); while
/// it waits it is <see cref="IsPathPending">pending</see> - it keeps a path it still has, or stands, and its order is never dropped. At the end of the update phase the world
/// works through the queue in the order the requests came, with a budget of <see cref="PathCellBudget"/> expanded cells per logic frame of the original (counted work,
/// never time): a search that does not fit pauses and continues in the next tick (<see cref="NavigationGrid.Slice"/>), and begins again when a footprint changed meanwhile.
/// </para>
/// <para>
/// <b>The shared group path:</b> a request that arrives in the same tick as a queued one with the same surfaces, a goal within <see cref="ShareGoalCells"/> cells and a start
/// within <see cref="ShareStartCells"/> cells does not search: it follows the other one's path from the first of its points a straight walk reaches, and leaves it for its own
/// goal by a straight walk. When the walks are not clear, or the leader's path does not arrive, the follower is queued with a search of its own.
/// </para>
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>The cells the queue may expand per logic frame of the original (two ticks at 60 Hz, so 60,000 a tick there): at about 75 ns a cell, 4.5 ms of a 60 Hz tick.</summary>
    public const int PathCellBudget = 120_000;

    /// <summary>A follower's goal may lie this many cells (Chebyshev) from the leader's.</summary>
    public const int ShareGoalCells = 8;

    /// <summary>A follower's start may lie this many cells (Chebyshev) from the leader's.</summary>
    public const int ShareStartCells = 24;

    private sealed class PathRequest(uint requester, PathQuery query, long tick)
    {
        public uint Requester { get; } = requester;
        public PathQuery Query { get; } = query;
        public long Tick { get; } = tick;
        /// <summary>The footprints the search began on, and how many expansions it has done (a paused search).</summary>
        public ulong Obstacles { get; set; }
        public int Done { get; set; }
        public bool Begun { get; set; }
        /// <summary>Requests of the same tick that follow this one's path, in the order they came.</summary>
        public List<(uint Requester, PathQuery Query)> Followers { get; } = [];
    }

    private readonly List<PathRequest> _pathQueue = [];
    // Answers not yet taken, by requester: the path (null for an arriving-only question without one).
    private readonly SortedDictionary<uint, PathResult?> _pathAnswers = [];

    /// <summary>Whether ground units use the path queue: the movement profile of the rules.</summary>
    public bool PathsQueued => Templates.Rules.Movement == MovementProfile.Budgeted;

    /// <summary>Counters of the queue for tools and tests (not state): requests, of them answered by a shared group path, cells worked, restarts, ticks that ended with work left.</summary>
    public PathQueueStats PathStats { get; } = new();

    /// <summary>Queues a path question of <paramref name="requester"/>; an earlier question of the same requester that is still open is replaced.</summary>
    public void RequestPath(Entity requester, PathQuery query)
    {
        CancelPath(requester);
        PathStats.Requests++;
        if (Navigation is null) return;
        // The grid's memory of answers is not asked: it is not state (a restored world has none), and here it would decide when an answer comes, not only how fast.
        if (query.Blocks.IsDefaultOrEmpty && !query.ArrivingOnly)
        {
            var start = (TerrainMap.CellOf(query.StartX), TerrainMap.CellOf(query.StartY));
            var goal = (TerrainMap.CellOf(query.GoalX), TerrainMap.CellOf(query.GoalY));
            foreach (var queued in _pathQueue)
            {
                var other = queued.Query;
                if (queued.Tick != _tick || other.Surfaces != query.Surfaces || !other.Blocks.IsDefaultOrEmpty || other.ArrivingOnly) continue;
                if (Chebyshev(goal, (TerrainMap.CellOf(other.GoalX), TerrainMap.CellOf(other.GoalY))) > ShareGoalCells) continue;
                if (Chebyshev(start, (TerrainMap.CellOf(other.StartX), TerrainMap.CellOf(other.StartY))) > ShareStartCells) continue;
                queued.Followers.Add((requester.Id.Value, query));
                return;
            }
        }
        _pathQueue.Add(new PathRequest(requester.Id.Value, query, _tick));
    }

    private static int Chebyshev((int X, int Y) a, (int X, int Y) b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>Whether <paramref name="requester"/> has a question in the queue (or follows one) whose answer is not there yet.</summary>
    public bool IsPathPending(Entity requester)
    {
        var id = requester.Id.Value;
        foreach (var request in _pathQueue)
        {
            if (request.Requester == id) return true;
            foreach (var follower in request.Followers)
                if (follower.Requester == id) return true;
        }
        return false;
    }

    /// <summary>The answer to <paramref name="requester"/>'s question, once it is there; it is taken out of the world.</summary>
    public bool TryTakePath(Entity requester, out PathResult? result) => _pathAnswers.Remove(requester.Id.Value, out result);

    /// <summary>Withdraws <paramref name="requester"/>'s question and an answer it has not taken.</summary>
    public void CancelPath(Entity requester)
    {
        var id = requester.Id.Value;
        _pathAnswers.Remove(id);
        for (var index = 0; index < _pathQueue.Count; index++)
        {
            var request = _pathQueue[index];
            request.Followers.RemoveAll(follower => follower.Requester == id);
            if (request.Requester != id) continue;
            // Its followers become questions of their own, in their order, where it stood.
            var replacement = request.Followers.Select(follower => new PathRequest(follower.Requester, follower.Query, request.Tick)).ToList();
            if (index == 0 && request.Begun) Navigation?.DropSlice();
            _pathQueue.RemoveAt(index);
            _pathQueue.InsertRange(index, replacement);
            return;
        }
    }

    /// <summary>The queue's work of a tick, at the end of the update phase: searches in the order they came until the budget of the tick is spent.</summary>
    private void ProcessPathQueue()
    {
        if (Navigation is not { } grid) return;
        // A requester that left the world asks nothing any more; nor does an answer wait for it.
        for (var index = _pathQueue.Count - 1; index >= 0; index--)
        {
            var request = _pathQueue[index];
            request.Followers.RemoveAll(follower => FindEntity(new EntityId(follower.Requester)) is null);
            if (FindEntity(new EntityId(request.Requester)) is not null) continue;
            var replacement = request.Followers.Select(follower => new PathRequest(follower.Requester, follower.Query, request.Tick)).ToList();
            if (index == 0 && request.Begun) grid.DropSlice();
            _pathQueue.RemoveAt(index);
            _pathQueue.InsertRange(index, replacement);
        }
        if (_pathAnswers.Count > 0)
            foreach (var gone in _pathAnswers.Keys.Where(id => FindEntity(new EntityId(id)) is null).ToList()) _pathAnswers.Remove(gone);
        var budget = Math.Max(1, PathCellBudget / Time.TicksPerOriginalFrame);
        while (_pathQueue.Count > 0 && budget > 0)
        {
            var request = _pathQueue[0];
            var restart = request.Begun && request.Obstacles != grid.ObstacleSum;
            if (restart) PathStats.Restarts++;
            if (!request.Begun || restart)
            {
                request.Begun = true;
                request.Obstacles = grid.ObstacleSum;
                request.Done = 0;
            }
            var ended = grid.Slice(request.Query, restart, request.Done, request.Done + budget, out var result, out var done, out var spent);
            request.Done = done;
            budget -= Math.Max(1, spent);
            PathStats.Cells += spent;
            if (!ended) break;
            _pathQueue.RemoveAt(0);
            Answer(grid, request, result);
        }
        if (_pathQueue.Count > 0) PathStats.TicksWithWorkLeft++;
    }

    private void Answer(NavigationGrid grid, PathRequest request, PathResult? result)
    {
        _pathAnswers[request.Requester] = result;
        if (request.Followers.Count == 0) return;
        var requeue = new List<PathRequest>();
        foreach (var (follower, query) in request.Followers)
        {
            if (result is { Status: PathStatus.Found } path && SharedPath(grid, path, query) is { } shared)
            {
                PathStats.Shared++;
                _pathAnswers[follower] = shared;
            }
            else requeue.Add(new PathRequest(follower, query, request.Tick));
        }
        // A follower that cannot share asks again, at the front: it waited as long as the one it followed.
        _pathQueue.InsertRange(0, requeue);
    }

    /// <summary>The follower's path along the leader's: from its start to the first leader point a straight walk reaches, the leader's points, then on to its own goal.</summary>
    private static PathResult? SharedPath(NavigationGrid grid, PathResult leader, PathQuery follower)
    {
        var points = leader.Points;
        var join = -1;
        for (var index = 0; index < points.Length; index++)
            if (grid.StraightWalkClear(follower.StartX, follower.StartY, points[index].X, points[index].Y, follower.Surfaces)) { join = index; break; }
        if (join < 0) return null;
        var end = points[^1];
        if (!grid.StraightWalkClear(end.X, end.Y, follower.GoalX, follower.GoalY, follower.Surfaces)) return null;
        var builder = ImmutableArray.CreateBuilder<PathPoint>(points.Length - join + 1);
        for (var index = join; index < points.Length - 1; index++) builder.Add(points[index]);
        builder.Add(new PathPoint(follower.GoalX, follower.GoalY));
        return new PathResult(PathStatus.Found, builder.ToImmutable());
    }

    /// <summary>The queue in the state: written only when there is something in it, so a world of the original profile (or an idle queue) keeps its serialized form.</summary>
    private void DescribePathQueue(IStateWriter writer)
    {
        if (_pathQueue.Count == 0 && _pathAnswers.Count == 0) return;
        writer.BeginArray("pathQueue");
        foreach (var request in _pathQueue)
        {
            writer.BeginObject(null);
            writer.UInt("requester", request.Requester);
            DescribeQuery(writer, request.Query);
            writer.Int("tick", request.Tick);
            writer.Bool("begun", request.Begun);
            writer.UInt("obstacles", request.Obstacles);
            writer.Int("done", request.Done);
            writer.BeginArray("followers");
            foreach (var (follower, query) in request.Followers)
            {
                writer.BeginObject(null);
                writer.UInt("requester", follower);
                DescribeQuery(writer, query);
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("pathAnswers");
        foreach (var (requester, result) in _pathAnswers)
        {
            writer.BeginObject(null);
            writer.UInt("requester", requester);
            writer.Bool("hasPath", result is not null);
            if (result is not null)
            {
                writer.Int("status", (int)result.Status);
                writer.BeginArray("points");
                foreach (var point in result.Points)
                {
                    writer.BeginObject(null);
                    writer.Fixed("x", point.X);
                    writer.Fixed("y", point.Y);
                    writer.EndObject();
                }
                writer.EndArray();
            }
            writer.EndObject();
        }
        writer.EndArray();
    }

    private static void DescribeQuery(IStateWriter writer, PathQuery query)
    {
        writer.Fixed("startX", query.StartX);
        writer.Fixed("startY", query.StartY);
        writer.Fixed("goalX", query.GoalX);
        writer.Fixed("goalY", query.GoalY);
        writer.Int("surfaces", (int)query.Surfaces);
        writer.Bool("arrivingOnly", query.ArrivingOnly);
        writer.BeginArray("blocks");
        if (!query.Blocks.IsDefaultOrEmpty)
            foreach (var (x, y, radius) in query.Blocks)
            {
                writer.BeginObject(null);
                writer.Fixed("x", x);
                writer.Fixed("y", y);
                writer.Fixed("radius", radius);
                writer.EndObject();
            }
        writer.EndArray();
    }

    private static PathQuery LoadQuery(IStateReader reader)
        => new(reader.Fixed("startX"), reader.Fixed("startY"), reader.Fixed("goalX"), reader.Fixed("goalY"), (LocomotorSurfaces)checked((int)reader.Int("surfaces")),
            [..reader.Array("blocks").Select(block => (block.Fixed("x"), block.Fixed("y"), block.Fixed("radius")))], reader.Bool("arrivingOnly"));

    private void LoadPathQueue(IStateReader state)
    {
        _pathQueue.Clear();
        _pathAnswers.Clear();
        if (!state.Has("pathQueue")) return;
        foreach (var item in state.Array("pathQueue"))
        {
            var request = new PathRequest(checked((uint)item.UInt("requester")), LoadQuery(item), item.Int("tick"))
            {
                Begun = item.Bool("begun"), Obstacles = item.UInt("obstacles"), Done = checked((int)item.Int("done")),
            };
            foreach (var follower in item.Array("followers")) request.Followers.Add((checked((uint)follower.UInt("requester")), LoadQuery(follower)));
            _pathQueue.Add(request);
        }
        foreach (var item in state.Array("pathAnswers"))
        {
            var requester = checked((uint)item.UInt("requester"));
            _pathAnswers[requester] = item.Bool("hasPath")
                ? new PathResult((PathStatus)checked((int)item.Int("status")), [..item.Array("points").Select(point => new PathPoint(point.Fixed("x"), point.Fixed("y")))])
                : null;
        }
    }
}

/// <summary>Counters of the path queue of the movement profile, for tools and tests; not part of the world state.</summary>
public sealed class PathQueueStats
{
    public long Requests { get; internal set; }
    public long Shared { get; internal set; }
    public long Cells { get; internal set; }
    public long Restarts { get; internal set; }
    public long TicksWithWorkLeft { get; internal set; }
}

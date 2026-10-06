using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

/// <summary>
/// The queued path of one unit in world space: from where the unit stands through every station still ahead (the one it is moving to first).
/// One marker per station, one segment between consecutive points (<c>W3DWaypointBuffer::drawWaypoints</c>: a line from the object through the
/// remaining goal path positions and a node model on each).
/// </summary>
public sealed record WaypointPath(EntityId Unit, Vector2 Start, ImmutableArray<Vector2> Stations);

public enum OrderHintKind
{
    /// <summary><c>InGameUI::createMoveHint</c>: the marker at the destination of a move.</summary>
    Move,
    Waypoint,
    RallyPoint,
    /// <summary>On the attacked object. The original marks it by flashing the target, not by a ground marker; here it is one.</summary>
    Attack,
    Enter,
}

/// <summary>A short-lived marker where an order was given. It follows <see cref="Target"/> while that object exists, otherwise it stays at <see cref="Position"/>.</summary>
public sealed record OrderHint(OrderHintKind Kind, Vector3 Position, EntityId Target, long BornMilliseconds, long LifetimeMilliseconds)
{
    /// <summary>0 when just given, 1 when it expires.</summary>
    public float Age(long nowMilliseconds) => LifetimeMilliseconds <= 0 ? 1f : Math.Clamp((nowMilliseconds - BornMilliseconds) / (float)LifetimeMilliseconds, 0f, 1f);
}

/// <summary>
/// The order hints on screen. A <see cref="CommandFeedback"/> marker lives for its <see cref="CommandFeedback.DurationFrames"/> client frames
/// at the original's 30 frames per second (<c>W3DInGameUI::drawMoveHints</c>: 40 frames); at most <see cref="Capacity"/> are kept, the oldest
/// making room (<c>InGameUI::MAX_MOVE_HINTS</c> = 256, a ring).
/// </summary>
public sealed class OrderHintBook
{
    public const int Capacity = 256;
    private const int ClientFramesPerSecond = OriginalFrame.PerSecond;
    private readonly List<OrderHint> _hints = [];

    /// <summary>Takes the feedback kinds that mark a place or an object; everything else (rejections, unsupported orders) is not a marker and returns false.</summary>
    public bool Observe(CommandFeedback feedback, long nowMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        OrderHintKind? kind = feedback.Kind switch
        {
            FeedbackKind.MoveMarker => OrderHintKind.Move,
            FeedbackKind.WaypointMarker => OrderHintKind.Waypoint,
            FeedbackKind.RallyMarker => OrderHintKind.RallyPoint,
            FeedbackKind.AttackMarker => OrderHintKind.Attack,
            FeedbackKind.EnterMarker => OrderHintKind.Enter,
            _ => null,
        };
        if (kind is not { } hintKind) return false;
        if (_hints.Count == Capacity) _hints.RemoveAt(0);
        _hints.Add(new OrderHint(hintKind, feedback.Position, feedback.Target, nowMilliseconds, feedback.DurationFrames * 1000L / ClientFramesPerSecond));
        return true;
    }

    /// <summary>The hints still alive at <paramref name="nowMilliseconds"/>, oldest first; expired ones are dropped.</summary>
    public ImmutableArray<OrderHint> Active(long nowMilliseconds)
    {
        _hints.RemoveAll(hint => nowMilliseconds - hint.BornMilliseconds >= hint.LifetimeMilliseconds);
        return [.._hints];
    }

    public void Clear() => _hints.Clear();
}

public static class OrderMarkers
{
    /// <summary>
    /// The waypoint paths to show: those of the selected own units, and only while waypoint mode is held (<c>W3DWaypointBuffer::drawWaypoints</c>
    /// draws them under <c>isInWaypointMode()</c> for the selected drawables). A station disappears when the unit reaches it, the whole path
    /// when the unit gets another order (the standing order is replaced or cleared) or dies.
    /// </summary>
    public static ImmutableArray<WaypointPath> WaypointPaths(SimulationWorld world, SelectionModel selection, StandingOrders orders, bool waypointMode)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(orders);
        if (!waypointMode) return [];
        var paths = ImmutableArray.CreateBuilder<WaypointPath>();
        foreach (var unit in selection.OwnSelected(world))
        {
            if (!orders.TryGet(unit.Id, out var order) || order.Kind != StandingOrderKind.Waypoints) continue;
            paths.Add(new WaypointPath(unit.Id, SimulationQueries.GroundOf(unit), [order.Destination, ..order.Waypoints]));
        }
        return paths.ToImmutable();
    }
}

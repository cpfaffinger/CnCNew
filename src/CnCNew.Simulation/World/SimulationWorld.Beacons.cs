using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// The beacons of a match of several humans (<c>GameLogic::onPlaceBeacon</c>, <c>onRemoveBeacon</c>): an object of the player's template <c>BeaconName</c> where the player points, at most
/// <c>MaxBeaconsPerPlayer</c> of them. Only the presentation tells them from other objects: a non-ally does not see them (<c>BeaconClientUpdate::hideBeacon</c>). The world does not know
/// whether a match has several humans; the client offers the button only there (<c>GameLogic::isInMultiplayerGame</c>).
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>An object made by a player's beacon order.</summary>
    public bool IsBeacon(Entity entity) => _templates.BeaconTemplates.Contains(entity.Template.Name);

    /// <summary>How many beacons a player has up (<c>countObjectsByThingTemplate</c>).</summary>
    public int BeaconCount(PlayerId player)
    {
        if (BeaconTemplateOf(player) is not { } template) return 0;
        var count = 0;
        foreach (var entity in _entities.Values)
            if (entity.Owner == player && ReferenceEquals(entity.Template, template) && !entity.DestroyRequested) count++;
        return count;
    }

    private ObjectTemplate? BeaconTemplateOf(PlayerId player)
        => FactionOf(player) is { BeaconName.Length: > 0 } faction && _templates.TryGet(faction.BeaconName, out var template) ? template : null;

    private CommandRejection CheckPlaceBeacon(PlayerId issuer)
    {
        if (BeaconTemplateOf(issuer) is null || IsPlayerDefeated(issuer)) return CommandRejection.NotPermitted;
        return BeaconCount(issuer) >= _templates.MaxBeaconsPerPlayer ? CommandRejection.TooManyBeacons : 0;
    }

    private void PlaceBeacon(PlaceBeaconCommand order)
    {
        var template = BeaconTemplateOf(order.Player)!;
        // getExtent, findClosestEdgePoint: a point beyond the map stands on its edge.
        var (x, y) = (order.X, order.Y);
        if (Terrain is { } terrain) (x, y) = terrain.ClosestPointOnMap(x, y);
        var beacon = SpawnEntity(new PendingSpawn(Ids.AllocateEntity(), template.Index, order.Player, x, y, GroundHeightAt(x, y), Fixed.Zero));
        Emit(SimEventKind.BeaconPlaced, beacon.Id, default, string.Empty, 0, order.Player);
    }

    /// <summary>The owner's beacons among the selection go; what is another's is that player's own view (<c>hideBeacon</c>), which the client does for itself.</summary>
    private void RemoveBeacons(RemoveBeaconCommand order)
    {
        foreach (var id in order.Units)
            if (FindEntity(id) is { } entity && entity.Owner == order.Player && IsBeacon(entity)) RequestDestroy(id);
    }
}

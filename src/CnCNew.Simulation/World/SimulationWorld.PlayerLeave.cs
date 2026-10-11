using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary>
    /// <c>MSG_SELF_DESTRUCT</c> in <c>GameLogic::logicMessageDispatcher</c> (<c>GameLogicDispatch.cpp</c>). With <paramref name="transferToAlly"/> the players are walked in
    /// list order for the first one that is allied with the leaver in both directions and not defeated (<c>VictoryConditions::hasSinglePlayerBeenDefeated</c>); it takes over
    /// (<c>Player::transferAssetsFromThat</c>: every object changes its team, all money moves), and <c>Player::killPlayer</c> runs afterwards "just to be safe". Without the
    /// flag, or without such an ally, only <c>killPlayer</c> runs. (docs/SIMULATION-CORE.md §47)
    /// </summary>
    private void ExecutePlayerLeave(PlayerId leaver, bool transferToAlly)
    {
        if (transferToAlly)
        {
            foreach (var other in _playerList)
            {
                if (other.Id.IsSystem || other.Id == leaver || !AreAllies(leaver, other.Id) || IsPlayerDefeated(other.Id)) continue;
                foreach (var entity in EntitiesOwnedBy(leaver).AsSpan().ToArray())
                    TransferOwnership(entity.Id, other.Id);
                AddMoney(other.Id, WithdrawMoney(leaver, MoneyOf(leaver)));
                break;
            }
        }
        KillPlayer(leaver);
    }

    /// <summary>
    /// <c>Player::killPlayer</c> through <c>Team::killTeam</c>: first every living object of the player that holds riders lets all of them out at once
    /// (<c>Team::evacuateTeam</c>, <c>removeAllContained</c> - not through the door), then every object that is still the player's and not already dead is killed
    /// (<c>Object::kill</c>); a <c>TECH_BUILDING</c> is handed to the neutral player instead. The original's special case for beacons (effectively dead, destroyed directly) has
    /// no counterpart: this simulation has no beacons.
    /// </summary>
    private void KillPlayer(PlayerId player)
    {
        var owned = EntitiesOwnedBy(player).AsSpan().ToArray();
        foreach (var container in owned)
        {
            if (container.DestroyRequested || container.EffectivelyDead || container.Container is not { } contain || contain.Contained.Count == 0) continue;
            foreach (var riderId in contain.Contained.ToArray())
            {
                var rider = FindEntity(riderId);
                if (rider is null) continue;
                contain.Remove(this, rider, true);
                rider.ContainedBy = EntityId.Invalid;
                MarkDirty(rider);
                Emit(SimEventKind.EntityUncontained, rider.Id, container.Id, string.Empty, 0);
            }
            MarkDirty(container);
        }
        // The riders that came out are the player's too when they were; the list is read again, in id order.
        foreach (var entity in EntitiesOwnedBy(player).AsSpan().ToArray())
        {
            if (entity.DestroyRequested || entity.EffectivelyDead || entity.Owner != player) continue;
            if (entity.Template.IsKindOf("TECH_BUILDING")) TransferOwnership(entity.Id, PlayerId.System);
            else Kill(entity.Id);
        }
    }
}

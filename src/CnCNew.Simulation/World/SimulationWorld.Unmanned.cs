using CnCNew.Data.Definitions;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.World;

/// <summary>
/// <c>DISABLED_UNMANNED</c>: a vehicle whose driver is dead (<c>DAMAGE_KILLPILOT</c> of Jarmen Kell's rifle, or a script) stops, goes to the neutral player and stands there until any infantry unit walks
/// into it, which makes the vehicle its player's and is the end of that infantry unit (<c>PhysicsBehavior::onCollide</c>).
/// </summary>
public sealed partial class SimulationWorld
{
    private static readonly uint CarBombSetMask = 1u << (NameTablesPhase5.WeaponSetCondition.TryIndexOf("CARBOMB", out var carBomb) ? carBomb : throw new InvalidOperationException("CARBOMB is not a weapon set condition."));

    /// <inheritdoc cref="IWorldServices.KillPilot"/>
    public void KillPilot(Entity victim, in DamageInfo info)
    {
        if (!victim.Template.IsKindOf("VEHICLE")) return;
        // "We actually will kill the bike by forcing the rider to leave the bike. That way the bike will automatically scuttle and be unusable."
        if (victim.Container is RiderChangeContain bike)
        {
            if (victim.HasMoveTarget) Kill(victim.Id, info.Source);
            else if (bike.ContainCount > 0 && FindEntity(bike.Contained[0]) is { } rider)
            {
                bike.Remove(this, rider, true);
                Kill(rider.Id, info.Source);
            }
            return;
        }
        Unman(victim, info.Source);
    }

    /// <summary>
    /// <c>Object::setDisabled</c>, <c>TheGameLogic->deselectObject</c>, <c>aiIdle</c> and <c>setTeam(neutral)</c>: "if I am a carbomb, my driver actually has a dead-man's trigger for my dynamite"; every
    /// other vehicle loses its veterancy and what it healed by, stops and throws its passengers out (<c>TransportContain::onCapture</c>).
    /// </summary>
    public void Unman(Entity vehicle, EntityId sniper)
    {
        if (vehicle.Unmanned) return;
        vehicle.Unmanned = true;
        if (!vehicle.Template.IsKindOf("DRONE"))
        {
            Emit(SimEventKind.PilotKilled, vehicle.Id, sniper, string.Empty, 0);
            if (vehicle.Template.WeaponSets.Any(set => (set.Conditions & CarBombSetMask) != 0))
            {
                Kill(vehicle.Id, sniper);
                return;
            }
            SetVeterancy(vehicle, 0);
            vehicle.Experience = 0;
            foreach (var module in vehicle.ModuleArray)
                if (module is AutoHealBehavior heal) heal.UndoUpgrade();
        }
        AiOf(vehicle)?.OrderIdle(this);
        if (vehicle.Container is { } container)
            foreach (var id in container.Contained.ToArray())
                if (FindEntity(id) is { } rider) container.Remove(this, rider, true);
        TransferOwnership(vehicle.Id, PlayerId.System);
        MarkDirty(vehicle);
    }

    /// <summary><c>ActionManager::canEnterObject</c>: "Any infantry unit can take over any unmanned vehicle!"</summary>
    public bool CanTakeOver(Entity actor, Entity vehicle)
        => vehicle.Unmanned && actor.Template.IsKindOf("INFANTRY") && !actor.Template.IsKindOf("REJECT_UNMANNED");

    /// <inheritdoc cref="IWorldServices.TakesOver"/>
    public bool TakesOver(Entity infantry, Entity vehicle)
    {
        if (!CanTakeOver(infantry, vehicle)) return false;
        TakeOver(infantry, vehicle);
        return true;
    }

    /// <summary>The vehicle is the taker's now, and "we are going to transfer the name of the infantry to the vehicle"; the infantry unit is its new driver and gone.</summary>
    public void TakeOver(Entity taker, Entity vehicle)
    {
        vehicle.Unmanned = false;
        vehicle.Captured = true;
        TransferOwnership(vehicle.Id, taker.Owner);
        MarkDirty(vehicle);
        if (!taker.Template.IsKindOf("INFANTRY")) return;
        Emit(SimEventKind.ObjectNameTransferred, taker.Id, vehicle.Id, string.Empty, 0);
        RequestDestroy(taker.Id);
    }

    /// <summary><c>Object::kill</c> by <paramref name="killer"/>, who scores it.</summary>
    private void Kill(EntityId victim, EntityId killer) => Damage(victim, new DamageInfo(killer, Fixed.Zero, DamageTypes.Unresistable, DeathTypes.Normal, true));
}

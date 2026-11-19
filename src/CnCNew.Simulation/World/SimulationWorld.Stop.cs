using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// The stop (#463, #466, #467, #470): <c>aiIdle</c> of one object (<c>AIUpdateInterface::privateIdle</c>) and <c>AIGroup::groupIdle</c> of a member of a group - the player's
/// <c>MSG_DO_STOP</c> and a script's <c>TEAM_STOP</c>.
/// </summary>
public sealed partial class SimulationWorld
{
    private static readonly int CanStealthStatus = ObjectStatusBits.Index("CAN_STEALTH");

    /// <summary>The salt of the stopped stealth unit's draw (<see cref="DelayLookAfterStop"/>), apart from the guard's.</summary>
    private const ulong StopLookSalt = 0x5709;

    /// <summary>Whether <c>groupIdle</c> does anything to the object: it has an AI or a carrier's deck, holds riders (a garrisoned building) or has slaves.</summary>
    public static bool TakesStop(Entity unit)
        => CanMove(unit) || DeckOf(unit) is not null || unit.Container is not null || Array.Exists(unit.ModuleArray, module => module is ISpawnerModule);

    /// <summary>
    /// <paramref name="asGroup"/>: <c>AIGroup::groupIdle</c> for one member - an object with an AI goes idle (<c>aiIdle</c>; a carrier's deck takes it as its idle order); a player's
    /// stop delays a stealth unit's next look until it can stealth again; an object without an AI lets its riders go idle (<c>makeMemberStop</c>, a garrisoned building); a spawner's
    /// slaves go idle (<c>SpawnBehavior::orderSlavesToGoIdle</c>). Without <paramref name="asGroup"/>: <c>aiIdle</c> alone (<c>ScriptActions::doNamedStop</c>).
    /// </summary>
    internal void StopObject(Entity unit, bool fromPlayer, bool asGroup)
    {
        if (unit.DestroyRequested) return;
        if (DeckOf(unit) is { } deck)
            // FlightDeckBehavior::aiDoCommand(AICMD_IDLE): the carrier's stop calls its jets home - those in the air and those not parked land on it (#467).
            deck.TakeOrder(this, DeckOrder.Idle, EntityId.Invalid, Fixed.Zero, Fixed.Zero);
        else if (AiOf(unit) is { } ai)
        {
            IdleUnit(unit, ai, fromPlayer);
            if (asGroup && fromPlayer) DelayLookAfterStop(unit, ai);
        }
        else if (asGroup && unit.Container is { } garrison)
            // makeMemberStop: "Handle garrisoned buildings."
            foreach (var id in garrison.Contained.ToArray())
                if (FindEntity(id) is { DestroyRequested: false } rider && AiOf(rider) is { } riderAi) IdleUnit(rider, riderAi, fromPlayer);
        if (!asGroup) return;
        // "Also handle slaves. If we have slaves, then order them to stop too!"
        foreach (var module in unit.ModuleArray)
        {
            if (module is not ISpawnerModule spawner) continue;
            foreach (var id in spawner.Slaves.ToArray())
                if (FindEntity(id) is { DestroyRequested: false } slave && AiOf(slave) is { } slaveAi) IdleUnit(slave, slaveAi, fromPlayer);
        }
    }

    /// <summary>
    /// <c>aiIdle</c> -> <c>privateIdle</c>: the object goes idle where the order finds it (<see cref="AIUpdateInterface.OrderStop"/>; a jet in its own way), and every rider of it with an AI
    /// goes idle as well ("iterate the contained items": a transport's or a fighting vehicle's riders end their attacks). A projectile ignores it.
    /// </summary>
    private void IdleUnit(Entity unit, AIUpdateInterface ai, bool fromPlayer)
    {
        if (unit.Template.IsKindOf("PROJECTILE")) return;
        ai.OrderStop(this, fromPlayer);
        if (unit.Container is not { } contain) return;
        foreach (var id in contain.Contained.ToArray())
            if (FindEntity(id) is { DestroyRequested: false } rider && AiOf(rider) is { } riderAi) IdleUnit(rider, riderAi, fromPlayer);
    }

    /// <summary>
    /// <c>AIGroup::groupIdle</c>, a player's stop: "When a stealth unit is currently not stealthed and doesn't autoacquire while stealthed, then when the player specifically orders the unit
    /// to stop, we want to not autoacquire until he is able to stealth again." A unit that can stealth (<c>OBJECT_STATUS_CAN_STEALTH</c>), looks for victims by itself
    /// (<c>canAutoAcquire</c>: any <c>AutoAcquireEnemiesWhenIdle</c> bit), is neither stealthed nor detected and does not look while stealthed (<c>canAutoAcquireWhileStealthed</c>: the
    /// <c>STEALTHED</c> bit, or a stealth granted by a special power) looks next after its stealth module's <c>StealthDelay</c> plus a draw of up to a second
    /// (<c>setNextMoodCheckTime</c>, which drops the random offset of the next interval). The draw is a hash of the world seed, the unit and the frame, in frames of the original, as the
    /// other mood draws of this simulation.
    /// </summary>
    private void DelayLookAfterStop(Entity unit, AIUpdateInterface ai)
    {
        if (!unit.HasStatus(CanStealthStatus) || ai.AutoAcquire == AutoAcquire.None) return;
        if (unit.HasStatus(StealthedStatus) || unit.HasStatus(DetectedStatus)) return;
        if (Array.Find(unit.ModuleArray, module => module is StealthUpdate) is not StealthUpdate stealth) return;
        if (stealth.GrantedBySpecialPower || (ai.AutoAcquire & AutoAcquire.Stealthed) != 0) return;
        var random = GuardScanOffset(unit, _tick, SimulationRate.OriginalFramesPerSecond, StopLookSalt);
        unit.Acquisition = new AcquisitionState { Mode = (byte)AcquireMode.Idle, NextCheck = _tick + stealth.StealthDelayTicks + random, Offset = false };
        _acquirers.Add(unit.Id.Value);
        MarkDirty(unit);
    }
}

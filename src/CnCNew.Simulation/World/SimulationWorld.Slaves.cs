using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.World;

/// <summary>
/// The slaves of a spawner take its attacks (F-65; <c>AIGroup::groupAttackObject</c> / <c>groupAttackPosition</c> -> <c>SpawnBehavior::orderSlavesToAttackTarget</c> /
/// <c>orderSlavesToAttackPosition</c>): a spawner whose slaves have no free will - the stinger site, the angry mob - hands an attack order on to every living slave with an AI.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>The living slaves of <paramref name="unit"/> that take its attacks: none for a spawner whose slaves have free will (<c>doSlavesHaveFreedom</c>).</summary>
    public IEnumerable<Entity> OrderedSlaves(Entity unit)
    {
        foreach (var module in unit.ModuleArray)
        {
            if (module is not ISpawnerModule { SlavesHaveFreeWill: false } spawner) continue;
            foreach (var id in spawner.Slaves)
                if (FindEntity(id) is { DestroyRequested: false, EffectivelyDead: false } slave && AiOf(slave) is not null) yield return slave;
        }
    }

    /// <summary>The unit passes an attack order on to its slaves: it has living slaves without free will.</summary>
    public bool PassesAttackToSlaves(Entity unit) => OrderedSlaves(unit).Any();

    /// <summary>
    /// <c>SpawnBehavior::orderSlavesToAttackTarget</c>: every slave force-attacks the victim (<c>aiForceAttackObject</c>) - it walks at it and fires until the victim is gone or out of its
    /// reach for good. A slave that already attacks this victim so keeps its attack (the client repeats an attack order while it lasts).
    /// </summary>
    private void PassAttackToSlaves(Entity spawner, Entity victim)
    {
        foreach (var slave in OrderedSlaves(spawner).ToArray())
        {
            if (slave.Acquisition is { Forced: true } running && running.Victim == victim.Id) continue;
            var ai = AiOf(slave)!;
            ai.OrderIdle(this, true);
            var state = slave.Acquisition ??= new AcquisitionState();
            state.Victim = victim.Id;
            state.Forced = true;
            AimTurretsForAttack(slave, victim, TurretAimOrder.Attack);
            _acquirers.Add(slave.Id.Value);
            MarkDirty(slave);
        }
    }

    /// <inheritdoc cref="Modules.IWorldServices.AttackAsAi"/>
    public void AttackAsAi(Entity unit, Entity victim)
    {
        if (AiOf(unit) is not { } ai) return;
        // A new state replaces the old one (its move, guard or attack), and the command source is the AI's.
        ai.OrderIdle(this, false);
        if (!IsAttackable(unit, victim) || !CanAttack(unit, victim, false, out _)) return;
        var state = unit.Acquisition ??= new AcquisitionState();
        state.Victim = victim.Id;
        AimTurretsForAttack(unit, victim, TurretAimOrder.Attack);
        _acquirers.Add(unit.Id.Value);
        MarkDirty(unit);
    }

    /// <inheritdoc cref="Modules.IWorldServices.MoodTargetAskedFor"/>
    public Entity? MoodTargetAskedFor(Entity unit)
        => unit.EffectivelyDead || unit.DestroyRequested || AiOf(unit) is not { } ai ? null : FindMoodTarget(unit, ai, AcquireMode.Idle, calledByAi: false);

    /// <summary><c>SpawnBehavior::orderSlavesToAttackPosition</c>: every slave attacks the place (F-64).</summary>
    private void PassPlaceToSlaves(Entity spawner, Fixed x, Fixed y, int shots)
    {
        foreach (var slave in OrderedSlaves(spawner).ToArray()) AttackPlace(slave, x, y, shots, fromPlayer: true);
    }
}

using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

/// <summary>
/// Two rule-limit gaps named in <c>docs/HANDOFF-PHASE10.md</c> point 10 / <c>docs/SIMULATION-CORE.md</c> §17 and §20: a command to flip
/// <see cref="OverchargeBehavior"/> (the module already runs, but nothing could ever reach it) and a direct structure capture whose power-pool
/// effect was entirely unmodeled (no ownership-change path existed outside the mission script engine's own <c>TransferOwnership</c>, which
/// moves only the static <c>EnergyProduction</c>, not a <see cref="PowerPlantUpgrade"/>/<see cref="OverchargeBehavior"/> bonus).
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>
    /// <c>GUI_COMMAND_TOGGLE_OVERCHARGE</c> (<c>CommandButton.ini</c>, e.g. the Americas Power Plant's command set): the object must be the
    /// issuer's own, alive, complete, and carry an <see cref="OverchargeBehavior"/> module (the field is typed since Phase 6 but had no command
    /// reaching it, see <c>docs/SIMULATION-CORE.md</c> §17).
    /// </summary>
    private CommandRejection CheckOvercharge(PlayerId issuer, OverchargeCommand command, out OverchargeBehavior? module)
    {
        module = null;
        var target = FindEntity(command.Target);
        if (target is null || target.DestroyRequested || target.EffectivelyDead) return CommandRejection.UnknownEntity;
        if (!MayCommand(issuer, target)) return CommandRejection.NotPermitted;
        if (target.HasStatus(ObjectStatusBits.UnderConstruction)) return CommandRejection.CannotToggleOvercharge;
        module = target.Modules.OfType<OverchargeBehavior>().FirstOrDefault();
        return module is null ? CommandRejection.CannotToggleOvercharge : 0;
    }

    /// <summary>Capture range: the same "close enough to work on it" margin <c>UpdateTask</c> (construction/repair) uses.</summary>
    private static readonly Fixed CaptureReachCells = Fixed.FromInt(2 * TerrainMap.CellSize);

    /// <summary>
    /// Reduced <c>ActionManager::canCaptureBuilding</c> (see <see cref="CommandTypes.Capture"/> for what is intentionally not ported: the
    /// channeled <c>SpecialAbilityUpdate</c> ability, its science/recharge gate and its stealth-detection check). What remains: the actor is the
    /// issuer's own, has an AI (so it is a mobile unit, not a static prop) and is not itself under construction; the target is a complete,
    /// unsold, alive <c>STRUCTURE</c> the issuer can see, owned by an enemy, or (<c>KINDOF_CAPTURABLE</c> and not an ally's); and the actor
    /// already stands within <see cref="CaptureReachCells"/> of it (no automatic walk-to-target, matching <c>FireWeaponCommand</c>'s own
    /// "direkter, expliziter" simplification in <c>docs/GAME-RULES.md</c> §3).
    /// </summary>
    private CommandRejection CheckCapture(PlayerId issuer, CaptureCommand command, out Entity? actor, out Entity? target)
    {
        target = null;
        actor = FindEntity(command.Actor);
        if (actor is null || actor.DestroyRequested || actor.EffectivelyDead) return CommandRejection.UnknownEntity;
        if (!MayCommand(issuer, actor)) return CommandRejection.NotPermitted;
        if (AiOf(actor) is null || actor.HasStatus(ObjectStatusBits.UnderConstruction)) return CommandRejection.CannotCapture;
        target = FindEntity(command.Target);
        if (target is null || target.DestroyRequested || target.EffectivelyDead) return CommandRejection.UnknownEntity;
        if (!target.Template.IsKindOf("STRUCTURE") || target.HasStatus(ObjectStatusBits.UnderConstruction) || target.HasStatus(ObjectStatusBits.Sold))
            return CommandRejection.CannotCapture;
        if (target.Owner == actor.Owner) return CommandRejection.CannotCapture;
        var relationship = RelationshipOf(actor.Owner, target.Owner);
        var capturable = relationship == Relationship.Enemies || (target.Template.IsKindOf("CAPTURABLE") && relationship != Relationship.Allies);
        if (!capturable) return CommandRejection.CannotCapture;
        if (!CanPlayerSee(issuer, target)) return CommandRejection.TargetNotVisible;
        var dx = target.X - actor.X;
        var dy = target.Y - actor.Y;
        var reach = target.Template.BoundingCircleRadius + CaptureReachCells;
        if (dx * dx + dy * dy > reach * reach) return CommandRejection.CannotCapture;
        return 0;
    }

    /// <summary>
    /// <c>Object::onCapture</c>'s power-pool effect (the only effect this reduced capture models, see <see cref="CommandTypes.Capture"/>): the
    /// static <c>EnergyProduction</c> moves with the structure exactly as the script engine's own <c>TransferOwnership</c> does, and in addition
    /// any currently-granted dynamic bonus (a completed <see cref="PowerPlantUpgrade"/>, an active <see cref="OverchargeBehavior"/>) moves too —
    /// the gap <c>docs/SIMULATION-CORE.md</c> §17/§20 names, since neither module previously had any ownership-change hook to react to. Not
    /// modeled: the production queue, upgrades and garrison contents stay with the structure as-is; no score, no idle-AI reset.
    /// </summary>
    private void ExecuteCapture(Entity actor, Entity target)
    {
        var oldOwner = target.Owner;
        var newOwner = actor.Owner;
        var movingBonus = 0;
        if (!target.HasStatus(ObjectStatusBits.UnderConstruction)) movingBonus += target.Template.EnergyProduction;
        if (target.Modules.OfType<PowerPlantUpgrade>().FirstOrDefault() is { Executed: true }) movingBonus += target.Template.EnergyBonus;
        if (target.Modules.OfType<OverchargeBehavior>().FirstOrDefault() is { Active: true }) movingBonus += target.Template.EnergyBonus;
        if (movingBonus != 0)
        {
            AddEnergyProduction(oldOwner, -movingBonus);
            AddEnergyProduction(newOwner, movingBonus);
        }
        target.Owner = newOwner;
        MarkDirty(target);
        MarkDirty(actor);
        Emit(SimEventKind.StructureCaptured, target.Id, actor.Id, string.Empty, oldOwner.Value, newOwner);
    }
}

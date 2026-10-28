using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

/// <summary>
/// Two rule-limit gaps named in <c>docs/handoffs/HANDOFF-PHASE10.md</c> point 10 / <c>docs/SIMULATION-CORE.md</c> §17 and §20: a command to flip
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
    /// Reduced <c>ActionManager::canCaptureBuilding</c> (see <see cref="CommandTypes.Capture"/>). The actor is the issuer's own, has an AI (so it
    /// is a mobile unit, not a static prop) and is not itself under construction; the target is a complete, unsold, alive <c>STRUCTURE</c> the
    /// issuer can see, owned by an enemy, or (<c>KINDOF_CAPTURABLE</c> and not an ally's) (<see cref="StagedCaptureRules.CanCapture"/>). An actor
    /// that carries the channeled <c>SpecialAbilityUpdate</c> capture ability (<c>docs/SIM-STAGED-CAPTURE.md</c>) additionally needs that power
    /// ready (its <c>UnpauseSpecialPowerUpgrade</c> applied, recharged) and walks to the building itself; any other actor must already stand
    /// within <see cref="CaptureReachCells"/> of it (no automatic walk-to-target, matching <c>FireWeaponCommand</c>'s own "direkter, expliziter"
    /// simplification in <c>docs/GAME-RULES.md</c> §3) and captures instantly.
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
        if (!StagedCaptureRules.CanCapture(this, actor, target)) return CommandRejection.CannotCapture;
        if (!CanPlayerSee(issuer, target)) return CommandRejection.TargetNotVisible;
        // A unit that carries the staged capture ability (SpecialAbilityUpdate) walks to the building itself and needs its power unpaused and recharged;
        // everything else keeps the reduced instant capture and must already stand in reach.
        if (StagedCaptureOf(actor) is { } staged)
            return SpecialPowerRouting.FindModule(actor, staged.PowerIndex) is { } power && power.IsReady(this) ? 0 : CommandRejection.SpecialPowerNotReady;
        var dx = target.X - actor.X;
        var dy = target.Y - actor.Y;
        var reach = target.Template.BoundingCircleRadius + CaptureReachCells;
        if (!SquaredReach.Of(reach).Contains(dx * dx + dy * dy)) return CommandRejection.CannotCapture;
        return 0;
    }

    /// <summary>The staged capture ability (<see cref="SpecialAbilityUpdate"/> of an infantry or Black Lotus capture power) of <paramref name="actor"/>, or null for a unit without one.</summary>
    internal SpecialAbilityUpdate? StagedCaptureOf(Entity actor)
    {
        foreach (var module in actor.ModuleArray)
            if (module is SpecialAbilityUpdate ability && ability.IsCapture(this)) return ability;
        return null;
    }

    /// <summary>An accepted <see cref="CaptureCommand"/>: the staged ability when the actor has one (approach, unpack, raise the flag, change owner, pack), otherwise the instant <see cref="ExecuteCapture"/>.</summary>
    private void ExecuteCaptureCommand(Entity actor, Entity target)
    {
        if (StagedCaptureOf(actor) is { } staged) staged.Begin(this, target);
        else ExecuteCapture(actor, target);
    }

    /// <summary><see cref="IWorldServices.CaptureStructure"/>.</summary>
    public void CaptureStructure(Entity actor, Entity target) => ExecuteCapture(actor, target);

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
        if (!target.HasStatus(ObjectStatusBits.UnderConstruction))
        {
            ApplyObjectEnergy(target, oldOwner, false);
            ApplyObjectEnergy(target, newOwner, true);
        }
        // PowerPlantUpgrade::onCapture and OverchargeBehavior::onCapture move nothing while the object is disabled.
        if (!target.DisabledEdge && target.Modules.OfType<PowerPlantUpgrade>().FirstOrDefault() is { Executed: true }) movingBonus += target.Template.EnergyBonus;
        if (!target.DisabledEdge && target.Modules.OfType<OverchargeBehavior>().FirstOrDefault() is { Active: true }) movingBonus += target.Template.EnergyBonus;
        if (movingBonus != 0)
        {
            AddEnergyProduction(oldOwner, -movingBonus);
            AddEnergyProduction(newOwner, movingBonus);
        }
        NotifyOwnerChange(target, newOwner);
        SetOwner(target, newOwner);
        MarkDirty(target);
        MarkDirty(actor);
        Emit(SimEventKind.StructureCaptured, target.Id, actor.Id, string.Empty, oldOwner.Value, newOwner);
    }
}

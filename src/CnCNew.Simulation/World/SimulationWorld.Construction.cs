using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// Construction, sale and repair of structures (<c>BuildAssistant</c>, <c>GameLogicDispatch</c> construct/cancel/sell messages,
/// <c>ProductionPrerequisite</c>, <c>Player::canBuild</c>). A structure under construction is a real entity with the
/// <c>UNDER_CONSTRUCTION</c> status whose health rises with the work its builder performs.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>
    /// <c>BuildAssistant.cpp</c>: <c>FRAMES_TO_ALLOW_SCAFFOLD</c> (<c>LOGICFRAMES_PER_SECOND * 1.5f</c>, 45 frames in which the scaffold stands) plus the 135 frames in which
    /// <c>BuildAssistant::update</c> takes <c>100 / TOTAL_FRAMES_TO_SELL_OBJECT</c> (<c>LOGICFRAMES_PER_SECOND * 3.0f</c> = 90) percent per frame off the construction percent until
    /// it reaches -50 %: 180 logic frames of the original from the sell order to the refund. The refund falls in the logic frame before that time is over (<see cref="BeginSale"/>):
    /// the frame of the order counts as the first of the 180, at every rate.
    /// </summary>
    private const long SaleDurationFrames = 45 + 135;

    /// <summary><see cref="SaleDurationFrames"/> in ticks of this world, less the one logic frame of the original by which the refund comes early; resolved once when the world is created.</summary>
    private readonly long _saleDurationTicks;

    /// <summary>A structure whose <c>BuildTime</c> is zero still takes one logic frame of the original to build, in ticks of this world.</summary>
    private readonly long _minimumBuildTicks;

    private readonly SortedSet<(long Tick, uint Id)> _sales = [];

    private static bool MayCommand(PlayerId issuer, Entity entity) => issuer.IsSystem || entity.Owner == issuer;

    private static Modules.BuiltIn.AIUpdateInterface? BuilderOf(Entity entity)
        => AiOf(entity) is { CanBuild: true } ai && !entity.HasStatus(ObjectStatusBits.UnderConstruction) ? ai : null;

    /// <summary>
    /// Public dry run of a <see cref="ConstructCommand"/> (`docs/PRESENTATION-INPUT.md` §10 point 1): the exact rejection the command would get if
    /// submitted now, with no side effect. Presentation used to rebuild this check from public data only (`SimulationQueries.CheckPlacement`); this
    /// is the authoritative version it approximated, so the two can never drift apart again.
    /// </summary>
    public CommandRejection CheckConstruct(PlayerId issuer, ConstructCommand construct) => CheckConstruct(issuer, construct, out _, out _);

    /// <summary>Shared by submission and execution: funds, prerequisites and the map change between the two.</summary>
    private CommandRejection CheckConstruct(PlayerId issuer, ConstructCommand construct, out Entity? builder, out ObjectTemplate? template)
    {
        template = null;
        builder = FindEntity(construct.Builder);
        if (builder is null || builder.DestroyRequested || builder.EffectivelyDead) return CommandRejection.UnknownEntity;
        if (!MayCommand(issuer, builder)) return CommandRejection.NotPermitted;
        if (BuilderOf(builder) is null) return CommandRejection.CannotConstruct;
        if (!_templates.TryGet(construct.Template, out var found) || !found.IsInstantiable) return CommandRejection.UnknownTemplate;
        template = found;
        if (!_players.TryGetValue(builder.Owner.Value, out var owner)) return CommandRejection.NotPermitted;
        if (!CanBuildTemplate(owner, builder, found) || !owner.CanBuildBase) return CommandRejection.CannotConstruct;
        if (!BuildableOf(found).Equals("Ignore_Prerequisites", StringComparison.OrdinalIgnoreCase) && !PrerequisitesSatisfied(owner, found))
            return CommandRejection.MissingPrerequisite;
        if (ExceedsMaxSimultaneousOfType(owner, found)) return CommandRejection.ProductionLimitReached;
        if (owner.Money < ProductionCostOf(owner, found)) return CommandRejection.InsufficientFunds;
        if (Navigation is { } navigation
            && (!navigation.IsFootprintLegal(found, construct.X, construct.Y, construct.Angle) || !IsPositionExploredBy(owner.Id, construct.X, construct.Y)))
            return CommandRejection.InvalidPlacement;
        return 0;
    }

    /// <summary><c>Player::canBuild</c> (buildable status) plus the builder's command set, approximated by the shared <c>Side</c>.</summary>
    private bool CanBuildTemplate(PlayerState owner, Entity builder, ObjectTemplate template)
    {
        if (!template.IsKindOf("STRUCTURE")) return false;
        if (BuildableOf(template).Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
        if (BuildableOf(template).Equals("Only_By_AI", StringComparison.OrdinalIgnoreCase) && !owner.IsComputer) return false;
        return template.Side.Length == 0 || builder.Template.Side.Length == 0 || template.Side.Equals(builder.Template.Side, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>ProductionPrerequisite::isSatisfied</c>: every science line needs all its sciences; every object line is satisfied by any of its
    /// alternatives among the owner's completed (not under construction) objects, matched by template name.
    /// </summary>
    private bool PrerequisitesSatisfied(PlayerState owner, ObjectTemplate template)
    {
        foreach (var line in template.Prerequisites)
        {
            if (line.IsScience)
            {
                foreach (var name in line.Alternatives)
                    if (!_templates.Sciences.TryGet(name, out var science) || !owner.CompletedSciences.Has(science.Index)) return false;
                continue;
            }
            var satisfied = false;
            foreach (var entity in _entities.Values)
            {
                if (entity.Owner != owner.Id || entity.HasStatus(ObjectStatusBits.UnderConstruction)) continue;
                foreach (var name in line.Alternatives)
                    if (entity.Template.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { satisfied = true; break; }
                if (satisfied) break;
            }
            if (!satisfied) return false;
        }
        return true;
    }

    /// <summary>
    /// <c>Player::canBuild</c> applied to a unit queued at a producer instead of a structure placed by a dozer: the template must not itself be a
    /// <c>STRUCTURE</c> (those are only ever built via <see cref="ConstructCommand"/>), and the same <c>Buildable</c>/<c>Side</c> rules apply.
    /// </summary>
    private bool CanProduceTemplate(PlayerState owner, Entity producer, ObjectTemplate template)
    {
        if (template.IsKindOf("STRUCTURE")) return false;
        if (BuildableOf(template).Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
        if (BuildableOf(template).Equals("Only_By_AI", StringComparison.OrdinalIgnoreCase) && !owner.IsComputer) return false;
        return template.Side.Length == 0 || producer.Template.Side.Length == 0 || template.Side.Equals(producer.Template.Side, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>Player::canBuildMoreOfType</c>: counts the player's own, not-yet-dead objects of the exact template (<c>MaxSimultaneousLinkKey</c>
    /// grouping is not modeled, see <see cref="ObjectTemplate.MaxSimultaneousOfType"/>) plus, for a non-structure template, every matching entry
    /// still queued in any of the player's <see cref="Modules.BuiltIn.ProductionUpdate"/>s (a structure can never sit in a factory queue).
    /// </summary>
    private bool ExceedsMaxSimultaneousOfType(PlayerState owner, ObjectTemplate template)
    {
        if (template.MaxSimultaneousOfType <= 0) return false;
        var count = 0;
        var countQueued = !template.IsKindOf("STRUCTURE");
        foreach (var entity in _entities.Values)
        {
            if (entity.Owner != owner.Id || entity.EffectivelyDead) continue;
            if (entity.Template.Index == template.Index) count++;
            if (countQueued && FindProductionModule(entity) is { } production) count += production.CountInQueue(template.Index);
            if (count >= template.MaxSimultaneousOfType) return true;
        }
        return count >= template.MaxSimultaneousOfType;
    }

    /// <summary>
    /// Everything <see cref="Commands.QueueProductionCommand"/> would check except funds and queue room: side/buildable/kind, prerequisites and
    /// the <c>MaxSimultaneousOfType</c> cap. A read-only filter for a caller choosing among candidate templates before spending a command — the
    /// retail reference-match scenario's random production picks use it so a template whose tech building does not exist in that scenario is
    /// never attempted (`docs/SIMULATION-CORE.md` §7a); it is equally useful to a skirmish AI or to presentation's command-bar availability.
    /// </summary>
    /// <summary><c>ObjectTypes::canBuildAny(player)</c> for one template: it is buildable at all for the player (the <c>Buildable</c> status after script overrides, the prerequisites) — no producer is needed.</summary>
    public bool PlayerHasPrerequisitesFor(PlayerId player, string templateName)
    {
        if (!_players.TryGetValue(player.Value, out var owner) || !_templates.TryGet(templateName, out var template) || !template.IsInstantiable) return false;
        if (BuildableOf(template).Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
        return BuildableOf(template).Equals("Ignore_Prerequisites", StringComparison.OrdinalIgnoreCase) || PrerequisitesSatisfied(owner, template);
    }

    public bool CanQueueProductionTemplate(PlayerId player, EntityId producerId, string templateName)
    {
        if (!_players.TryGetValue(player.Value, out var owner)) return false;
        if (!_templates.TryGet(templateName, out var template) || !template.IsInstantiable) return false;
        if (FindEntity(producerId) is not { } producer || producer.Owner != player) return false;
        if (!CanProduceTemplate(owner, producer, template)) return false;
        if (!BuildableOf(template).Equals("Ignore_Prerequisites", StringComparison.OrdinalIgnoreCase) && !PrerequisitesSatisfied(owner, template)) return false;
        return !ExceedsMaxSimultaneousOfType(owner, template);
    }

    private void StartConstruction(PlayerId issuer, Entity builder, ObjectTemplate template, ConstructCommand construct)
    {
        var constructionCost = ProductionCostOf(_players[builder.Owner.Value], template);
        _players[builder.Owner.Value].Money -= constructionCost;
        var site = SpawnEntity(new PendingSpawn(Ids.AllocateEntity(), template.Index, builder.Owner, construct.X, construct.Y, Fixed.Zero, construct.Angle), builder.Id);
        Emit(SimEventKind.ConstructionStarted, site.Id, builder.Id, template.Name, constructionCost.Raw, builder.Owner);
        BuilderOf(builder)!.OrderConstruct(this, site);
    }

    private (Entity? Site, CommandRejection Reason) CheckSite(PlayerId issuer, EntityId siteId)
    {
        var site = FindEntity(siteId);
        if (site is null || site.DestroyRequested || site.EffectivelyDead) return (null, CommandRejection.UnknownEntity);
        if (!MayCommand(issuer, site)) return (null, CommandRejection.NotPermitted);
        if (!site.HasStatus(ObjectStatusBits.UnderConstruction)) return (null, CommandRejection.CannotConstruct);
        return (site, 0);
    }

    private CommandRejection CheckResume(PlayerId issuer, ResumeConstructCommand resume, out Entity? builder, out Entity? site)
    {
        site = null;
        builder = FindEntity(resume.Builder);
        if (builder is null || builder.DestroyRequested || builder.EffectivelyDead) return CommandRejection.UnknownEntity;
        if (!MayCommand(issuer, builder)) return CommandRejection.NotPermitted;
        if (BuilderOf(builder) is null) return CommandRejection.CannotConstruct;
        var (found, reason) = CheckSite(issuer, resume.Site);
        site = found;
        if (reason != 0) return reason;
        return found!.Owner == builder.Owner ? 0 : CommandRejection.NotPermitted;
    }

    private CommandRejection CheckCancelConstruct(PlayerId issuer, CancelConstructCommand cancel, out Entity? site)
    {
        var (found, reason) = CheckSite(issuer, cancel.Site);
        site = found;
        return reason;
    }

    private CommandRejection CheckRepair(PlayerId issuer, RepairCommand repair, out Entity? builder, out Entity? target)
    {
        target = null;
        builder = FindEntity(repair.Builder);
        if (builder is null || builder.DestroyRequested || builder.EffectivelyDead) return CommandRejection.UnknownEntity;
        if (!MayCommand(issuer, builder)) return CommandRejection.NotPermitted;
        if (AiOf(builder) is not { CanRepair: true } || builder.HasStatus(ObjectStatusBits.UnderConstruction)) return CommandRejection.CannotConstruct;
        target = FindEntity(repair.Target);
        if (target is null || target.DestroyRequested || target.EffectivelyDead) return CommandRejection.UnknownEntity;
        if (target.Owner != builder.Owner) return CommandRejection.NotPermitted;
        if (!Modules.BuiltIn.AIUpdateInterface.IsRepairable(target)) return CommandRejection.CannotConstruct;
        return 0;
    }

    private CommandRejection CheckSell(PlayerId issuer, SellCommand sell, out Entity? target)
    {
        target = FindEntity(sell.Target);
        if (target is null || target.DestroyRequested) return CommandRejection.UnknownEntity;
        if (!MayCommand(issuer, target)) return CommandRejection.NotPermitted;
        if (!target.Template.IsKindOf("STRUCTURE") || target.EffectivelyDead || target.HasStatus(ObjectStatusBits.UnderConstruction) || target.HasStatus(ObjectStatusBits.Sold))
            return CommandRejection.CannotSell;
        return 0;
    }

    private CommandRejection CheckRally(PlayerId issuer, SetRallyPointCommand rally, out Entity? producer, out Modules.BuiltIn.ProductionUpdate? production)
    {
        production = null;
        producer = FindEntity(rally.Producer);
        if (producer is null || producer.DestroyRequested) return CommandRejection.UnknownEntity;
        if (!MayCommand(issuer, producer)) return CommandRejection.NotPermitted;
        production = FindProductionModule(producer);
        return production is null ? CommandRejection.CannotProduce : 0;
    }

    private static bool CanProduceNow(Entity producer)
        => !producer.HasStatus(ObjectStatusBits.UnderConstruction) && !producer.HasStatus(ObjectStatusBits.Sold);

    /// <summary>
    /// One tick of builder work (<c>DozerAIUpdate</c> calling <c>Object::setConstructionPercent</c>): health rises linearly from one point to
    /// the maximum, completing the structure after <c>BuildTime</c> ticks of work. True once the structure is complete.
    /// </summary>
    public bool AdvanceConstruction(Entity site)
    {
        if (!site.HasStatus(ObjectStatusBits.UnderConstruction)) return true;
        var total = Math.Max(site.Template.BuildTicks, _minimumBuildTicks);
        site.ConstructionWork++;
        var work = Math.Min(site.ConstructionWork, total);
        if (site.Body is IConstructionBody body)
        {
            var delta = Fixed.FromRaw(ConstructionHealthTarget(site, work, total) - ConstructionHealthTarget(site, work - 1, total));
            if (delta > Fixed.Zero) body.ChangeHealthForConstruction(this, delta);
        }
        MarkDirty(site);
        if (site.ConstructionWork < total) return false;
        CompleteConstruction(site);
        return true;
    }

    private static long ConstructionHealthTarget(Entity site, long work, long total)
    {
        var one = Fixed.One.Raw;
        var max = Math.Max(site.Body!.MaxHealth.Raw, one);
        // Raw health times work units: 128 bits, the product of a large health (the data has 99,999,999) and a long build leaves a machine word at a fine resolution.
        return one + (long)((Int128)(max - one) * work / total);
    }

    private void CompleteConstruction(Entity site)
    {
        site.Status &= ~(1UL << ObjectStatusBits.UnderConstruction);
        var builder = site.BuilderId;
        site.BuilderId = EntityId.Invalid;
        if (_players.TryGetValue(site.Owner.Value, out var owner)) owner.EnergyProduction += site.Template.EnergyProduction;
        foreach (var node in site.Updates)
            if (node.Module is Modules.BuiltIn.BuildCompleteModule) WakeAt(site, node.Update, _tick);
        MarkDirty(site);
        Emit(SimEventKind.ConstructionCompleted, site.Id, builder, string.Empty, 0, site.Owner);
    }

    private void CancelConstruction(Entity site)
    {
        var refund = site.HasStatus(ObjectStatusBits.Reconstructing) ? Fixed.Zero : _players.TryGetValue(site.Owner.Value, out var payer) ? ProductionCostOf(payer, site.Template) : site.Template.BuildCost;
        if (_players.TryGetValue(site.Owner.Value, out var owner)) owner.Money += refund;
        Emit(SimEventKind.ConstructionCancelled, site.Id, EntityId.Invalid, string.Empty, refund.Raw, site.Owner);
        if (site.Body is null) RequestDestroy(site.Id);
        else Kill(site.Id);
    }

    private void BeginSale(Entity target)
    {
        target.Status |= 1UL << ObjectStatusBits.Sold;
        target.SellCompleteTick = _tick + _saleDurationTicks;
        _sales.Add((target.SellCompleteTick, target.Id.Value));
        if (FindProductionModule(target) is { } production)
        {
            production.CancelAll(out var refund);
            if (_players.TryGetValue(target.Owner.Value, out var owner)) owner.Money += refund;
        }
        foreach (var module in target.ModuleArray)
            if (module is ISellModule sellable) sellable.OnSelling(this);
        MarkDirty(target);
    }

    private void ProcessSales(long tick)
    {
        while (_sales.Count > 0 && _sales.Min.Tick <= tick)
        {
            var (_, id) = _sales.Min;
            _sales.Remove(_sales.Min);
            var entity = FindEntity(new EntityId(id));
            if (entity is null || entity.DestroyRequested) continue;
            var payout = Fixed.FromInt((int)(entity.Template.BuildCost * _templates.SellPercentage).Floor());
            if (_players.TryGetValue(entity.Owner.Value, out var owner)) owner.Money += payout;
            Emit(SimEventKind.StructureSold, entity.Id, EntityId.Invalid, string.Empty, payout.Raw, entity.Owner);
            RequestDestroy(entity.Id);
        }
    }
}

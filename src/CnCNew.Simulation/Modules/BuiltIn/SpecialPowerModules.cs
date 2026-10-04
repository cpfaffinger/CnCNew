using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The special-power modules: the recharge state machine every power shares (<see cref="SpecialPowerModuleBase"/>), the concrete powers
/// (<c>SpecialAbility</c>, <c>OCLSpecialPower</c>, <c>CashBountyPower</c>, <c>CashHackSpecialPower</c>), the modules that wire them up
/// (<c>SpecialPowerCreate</c>, <c>GrantUpgradeCreate</c>, <c>GrantScienceUpgrade</c>, <c>UnpauseSpecialPowerUpgrade</c>,
/// <c>SpecialPowerCompletionDie</c>) and the gunship handler (<c>SpectreGunshipDeploymentUpdate</c>). See docs/GAME-RULES.md §6d.
/// </summary>
internal static class SpecialPowerModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<SpecialPowerBaseData>("SpecialAbility", ModuleOwnerKind.Object, (configuration, _) => SpecialPowerBaseData.Parse(configuration), data => new SpecialAbility(data)),
        new ModuleBinding<SpecialAbilityUpdateData>("SpecialAbilityUpdate", ModuleOwnerKind.Object, (configuration, _) => SpecialAbilityUpdateData.Parse(configuration), data => new SpecialAbilityUpdate(data)),
        new ModuleBinding<OclSpecialPowerData>("OCLSpecialPower", ModuleOwnerKind.Object, (configuration, _) => OclSpecialPowerData.Parse(configuration), data => new OclSpecialPower(data)),
        new ModuleBinding<CashBountyPowerData>("CashBountyPower", ModuleOwnerKind.Object, (configuration, _) => CashBountyPowerData.Parse(configuration), data => new CashBountyPower(data)),
        new ModuleBinding<CashHackData>("CashHackSpecialPower", ModuleOwnerKind.Object, (configuration, _) => CashHackData.Parse(configuration), data => new CashHackSpecialPower(data)),
        new ModuleBinding<NoFieldsData>("SpecialPowerCreate", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new SpecialPowerCreate()),
        new ModuleBinding<GrantUpgradeCreateData>("GrantUpgradeCreate", ModuleOwnerKind.Object, (configuration, _) => GrantUpgradeCreateData.Parse(configuration), data => new GrantUpgradeCreate(data)),
        new ModuleBinding<GrantScienceUpgradeData>("GrantScienceUpgrade", ModuleOwnerKind.Object, GrantScienceUpgradeData.Parse, data => new GrantScienceUpgrade(data)),
        new ModuleBinding<UnpauseSpecialPowerUpgradeData>("UnpauseSpecialPowerUpgrade", ModuleOwnerKind.Object, UnpauseSpecialPowerUpgradeData.Parse, data => new UnpauseSpecialPowerUpgrade(data)),
        new ModuleBinding<SpecialPowerCompletionDieData>("SpecialPowerCompletionDie", ModuleOwnerKind.Object, (configuration, _) => SpecialPowerCompletionDieData.Parse(configuration), data => new SpecialPowerCompletionDie(data)),
        new ModuleBinding<SpectreGunshipData>("SpectreGunshipDeploymentUpdate", ModuleOwnerKind.Object, (configuration, _) => SpectreGunshipData.Parse(configuration), data => new SpectreGunshipDeploymentUpdate(data)),
    ];

    public static string RequiredPower(ModuleConfiguration configuration)
    {
        var name = ModuleConfigReader.Text(configuration, "SpecialPowerTemplate");
        return name.Length > 0 ? name : throw new InvalidDataException($"Module '{configuration.Name}' names no SpecialPowerTemplate.");
    }
}

// ============================================================================================================ recharge state machine

/// <summary><c>SpecialPowerModuleData</c>: the power's template name plus <c>UpdateModuleStartsAttack</c> and <c>StartsPaused</c>. Sounds and <c>ScriptedSpecialPowerOnly</c> are presentation/AI.</summary>
public sealed record SpecialPowerBaseData(string PowerName, bool StartsAttack, bool StartsPaused) : ModuleData
{
    public static SpecialPowerBaseData Parse(ModuleConfiguration configuration) => new(
        SpecialPowerModules.RequiredPower(configuration),
        ModuleConfigReader.Bool(configuration, "UpdateModuleStartsAttack", false),
        ModuleConfigReader.Bool(configuration, "StartsPaused", false));
}

/// <summary>
/// <c>SpecialPowerModule.cpp</c>: <c>availableOnFrame</c> / <c>pausedCount</c> / <c>pausedOnFrame</c> are ported as written, including that a
/// <c>SharedSyncedTimer</c> power ignores its own pause and reads the player's shared ready frame instead. Not modelled: the disabled-object
/// check (no disabled-status model), the public timer / EVA / view-object / script notifications (presentation), academy statistics.
/// </summary>
public abstract class SpecialPowerModuleBase(SpecialPowerBaseData baseData) : SimModule, ISpecialPowerModule
{
    private long _availableOnFrame;
    private int _pausedCount;
    private long _pausedOnFrame;
    private int _power = -1;

    protected SpecialPowerBaseData BaseData => baseData;
    public int PowerIndex => _power;
    public bool StartsAttack => baseData.StartsAttack;
    public bool IsPaused => _pausedCount > 0;

    public override void OnSpawn(IWorldServices world)
    {
        _power = world.SpecialPowers.TryGet(baseData.PowerName, out var info)
            ? info.Index
            : throw new InvalidOperationException($"'{Entity.Template.Name}': special power '{baseData.PowerName}' is not a SpecialPower definition.");
        if (!Entity.HasStatus(ObjectStatusBits.UnderConstruction))
        {
            // A SharedSynced power's ready tick is established here, deterministically, instead of lazily on the first IsReady()
            // read: the skirmish AI polls IsReady() every live tick but ReplayRunner never runs AI code, so a read-triggered seed
            // would depend on when the AI happened to look, diverging a live match's StateHash from its own replay (see
            // docs/SIMULATION-CORE.md §12).
            if (info.SharedSynced) SharedReadyFrame(world);
            else StartRecharge(world);
        }
        if (baseData.StartsPaused) PauseCountdown(world, true);
        OnObjectCreated(world);
    }

    /// <summary>Hook that runs at the end of the constructor phase (<c>CashBountyPower::onObjectCreated</c>).</summary>
    protected virtual void OnObjectCreated(IWorldServices world) { }

    public abstract bool AcceptsTarget(SpecialPowerTargetKind kind);

    public bool IsReady(IWorldServices world)
    {
        if (world.SpecialPowers[_power].SharedSynced) return world.Tick >= SharedReadyFrame(world);
        return _pausedCount == 0 && world.Tick >= _availableOnFrame;
    }

    /// <summary>
    /// Public read of the recharge tick (`docs/PRESENTATION-INPUT.md` §10 point 2, HUD <c>ReadyFraction</c>): a shared power reads the player's
    /// shared ready frame exactly as <see cref="IsReady"/> does; this ignores <see cref="IsPaused"/> (a paused power still reports when it would
    /// otherwise be ready, matching the reference's own <c>availableOnFrame</c> field).
    /// </summary>
    public long AvailableOnFrame(IWorldServices world) => world.SpecialPowers[_power].SharedSynced ? SharedReadyFrame(world) : _availableOnFrame;

    public virtual void OnSpecialPowerCreation(IWorldServices world)
    {
        StartRecharge(world);
        if (world.SpecialPowers[_power].SharedSynced)
        {
            // Player::expressSpecialPowerReadyFrame: a freshly scienced shared power starts ready to fire.
            world.SetSharedReadyFrame(Entity.Owner, _power, world.Tick);
            _availableOnFrame = SharedReadyFrame(world);
        }
        if (baseData.StartsPaused) PauseCountdown(world, true);
    }

    public void SetReadyFrame(long frame) => _availableOnFrame = frame;

    public void PauseCountdown(IWorldServices world, bool pause)
    {
        if (pause)
        {
            if (_pausedCount == 0) _pausedOnFrame = world.Tick;
            _pausedCount++;
        }
        else if (_pausedCount > 0)
        {
            _pausedCount--;
            if (_pausedCount == 0) _availableOnFrame += world.Tick - _pausedOnFrame;
        }
    }

    public void Trigger(IWorldServices world) => StartRecharge(world);

    public void Use(IWorldServices world, in SpecialPowerTarget target)
    {
        SpecialPowerRouting.FindHandler(Entity, world, _power)?.Initiate(world, _power, target);
        if (!baseData.StartsAttack) Trigger(world);
        Apply(world, target);
    }

    /// <summary>The power-specific effect that runs after the base <c>doSpecialPower*</c> (initiate + trigger).</summary>
    protected virtual void Apply(IWorldServices world, in SpecialPowerTarget target) { }

    /// <summary><c>Player::getOrStartSpecialPowerReadyFrame</c>: a timer that does not exist yet starts counting now.</summary>
    private long SharedReadyFrame(IWorldServices world)
    {
        if (world.TryGetSharedReadyFrame(Entity.Owner, _power, out var frame)) return frame;
        frame = world.Tick + world.SpecialPowers[_power].ReloadTicks;
        world.SetSharedReadyFrame(Entity.Owner, _power, frame);
        return frame;
    }

    private void StartRecharge(IWorldServices world)
    {
        var info = world.SpecialPowers[_power];
        var ready = world.Tick + info.ReloadTicks;
        if (info.SharedSynced) world.SetSharedReadyFrame(Entity.Owner, _power, ready);
        else _availableOnFrame = ready;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("power", _power);
        writer.Int("availableOnFrame", _availableOnFrame);
        writer.Int("pausedCount", _pausedCount);
        writer.Int("pausedOnFrame", _pausedOnFrame);
    }

    public override void Load(IStateReader reader)
    {
        _power = checked((int)reader.Int("power"));
        _availableOnFrame = reader.Int("availableOnFrame");
        _pausedCount = checked((int)reader.Int("pausedCount"));
        _pausedOnFrame = reader.Int("pausedOnFrame");
    }
}

/// <summary><c>SpecialAbility</c>: the plain power whose effect is carried out entirely by an update-module handler (unit abilities, gunships).</summary>
public sealed class SpecialAbility(SpecialPowerBaseData data) : SpecialPowerModuleBase(data)
{
    public override string ModuleName => "SpecialAbility";
    public override bool AcceptsTarget(SpecialPowerTargetKind kind) => true;
}

// SpecialAbilityUpdate (the generic handler behind command-button unit abilities; staged for the two capture powers) is in SpecialAbilityStaging.cs.

// ============================================================================================================ OCLSpecialPower

/// <summary><c>OCLSpecialPowerModuleData</c>. <c>OCLAdjustPositionToPassable</c> needs a terrain/passability model and is not read; <c>ReferenceObject</c> is UI.</summary>
public sealed record OclSpecialPowerData(SpecialPowerBaseData Base, ImmutableArray<(string Science, string Ocl)> UpgradeOcls, string DefaultOcl, int CreateLocation) : ModuleData
{
    public static OclSpecialPowerData Parse(ModuleConfiguration configuration)
    {
        var location = ModuleConfigReader.Text(configuration, "CreateLocation");
        return new(
            SpecialPowerBaseData.Parse(configuration),
            ModuleConfigReader.Pairs(configuration, "UpgradeOCL"),
            ModuleConfigReader.Text(configuration, "OCL"),
            // OCLSpecialPowerModuleData ctor: m_createLoc = CREATE_AT_EDGE_NEAR_SOURCE.
            location.Length == 0 ? 0 : NameTablesPhase5.OclCreateLocation.TryIndexOf(location, out var index) ? index : throw new InvalidDataException($"Unknown OCL create location '{location}'."));
    }
}

/// <summary>
/// <c>OCLSpecialPower.cpp</c>: the effect is one <c>ObjectCreationList</c> — the first <c>UpgradeOCL</c> whose science the owner has, else <c>OCL</c> —
/// created at the origin <c>CreateLocation</c> selects (the three map-edge locations from the terrain extent: nearest edge point to the source or the target, farthest corner from the target
/// plus 300 up) with the target as the nugget's second point, which a <c>DeliverPayload</c> nugget flies for. A use without a target creates it at the source object (<c>doSpecialPower</c>). Without a terrain the three
/// map-edge locations cannot be placed: the power still triggers and recharges, and the missing delivery is reported as
/// <see cref="CreationSkipReasons.NoTerrainEdge"/>. <c>USE_OWNER_OBJECT</c> passes <c>createOwner=false</c>: the object itself is the transport of a <c>DeliverPayload</c> nugget (the reference passes
/// <c>createOwner=false</c>).
/// </summary>
public sealed class OclSpecialPower(OclSpecialPowerData data) : SpecialPowerModuleBase(data.Base)
{
    private static readonly Fixed AboveLocationHeight = Fixed.FromInt(300); // CREATE_ABOVE_LOCATION_HEIGHT

    public override string ModuleName => "OCLSpecialPower";
    public override bool AcceptsTarget(SpecialPowerTargetKind kind) => true;

    protected override void Apply(IWorldServices world, in SpecialPowerTarget target)
    {
        var ocl = FindOcl(world);
        if (ocl.Length == 0) return;
        if (target.Kind == SpecialPowerTargetKind.None)
        {
            // doSpecialPower: ObjectCreationList::create(ocl, owner, &creationCoord, &creationCoord, false) at the owner.
            world.CreateFromList(Entity, ocl, Entity.Id, false, Entity.X, Entity.Y, Entity.Z, Entity.X, Entity.Y, Entity.Z, false);
            return;
        }

        Fixed x = target.X, y = target.Y, z = Fixed.Zero;
        if (target.Kind == SpecialPowerTargetKind.Object)
        {
            var victim = world.FindEntity(target.Entity);
            if (victim is null) return;
            (x, y, z) = (victim.X, victim.Y, victim.Z);
        }
        z = target.Kind == SpecialPowerTargetKind.Object ? z : world.GroundHeightAt(x, y);

        // OCLSpecialPower::doSpecialPowerAtLocation: where the deliverer comes in, and the target it flies for.
        switch (NameTablesPhase5.OclCreateLocation.Canonical(data.CreateLocation))
        {
            case "CREATE_AT_LOCATION":
                world.CreateFromList(Entity, ocl, Entity.Id, false, x, y, z, x, y, z, true);
                break;
            case "USE_OWNER_OBJECT":
                world.CreateFromList(Entity, ocl, Entity.Id, false, x, y, z, x, y, z, false);
                break;
            case "CREATE_ABOVE_LOCATION":
                world.CreateFromList(Entity, ocl, Entity.Id, false, x, y, z + AboveLocationHeight, x, y, z, true);
                break;
            case "CREATE_AT_EDGE_NEAR_SOURCE" when world.Terrain is not null:
            {
                var (edgeX, edgeY) = FlightModules.ClosestEdgePoint(world, Entity.X, Entity.Y);
                world.CreateFromList(Entity, ocl, Entity.Id, false, edgeX, edgeY, world.GroundHeightAt(edgeX, edgeY), x, y, z, true);
                break;
            }
            case "CREATE_AT_EDGE_NEAR_TARGET" when world.Terrain is not null:
            {
                var (edgeX, edgeY) = FlightModules.ClosestEdgePoint(world, x, y);
                world.CreateFromList(Entity, ocl, Entity.Id, false, edgeX, edgeY, world.GroundHeightAt(edgeX, edgeY), x, y, z, true);
                break;
            }
            case "CREATE_AT_EDGE_FARTHEST_FROM_TARGET" when world.Terrain is not null:
            {
                var (edgeX, edgeY) = FlightModules.FarthestEdgePoint(world, x, y);
                world.CreateFromList(Entity, ocl, Entity.Id, false, edgeX, edgeY, world.GroundHeightAt(edgeX, edgeY) + AboveLocationHeight, x, y, z, true);
                break;
            }
            case var edge:
                world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, edge, CreationSkipReasons.NoTerrainEdge);
                break;
        }
    }

    private string FindOcl(IWorldServices world)
    {
        var sciences = world.PlayerSciences(Entity.Owner);
        foreach (var (science, ocl) in data.UpgradeOcls)
        {
            if (!world.Sciences.TryGet(science, out var info)) throw new InvalidOperationException($"OCLSpecialPower: '{science}' is not a Science definition.");
            if (sciences.Has(info.Index)) return ocl;
        }
        return data.DefaultOcl;
    }
}

// ============================================================================================================ CashBountyPower

public sealed record CashBountyPowerData(SpecialPowerBaseData Base, Fixed Bounty) : ModuleData
{
    public static CashBountyPowerData Parse(ModuleConfiguration configuration)
        => new(SpecialPowerBaseData.Parse(configuration), ModuleConfigReader.Real(configuration, "Bounty", Fixed.Zero));
}

/// <summary>
/// <c>CashBountyPower.cpp</c>: raises the owner's cash bounty ratio to <see cref="CashBountyPowerData.Bounty"/> when the object appears (only if the
/// owner has the power's science) and whenever the power's science is granted. The payout itself is <c>SimulationWorld.ScoreTheKill</c>.
/// </summary>
public sealed class CashBountyPower(CashBountyPowerData data) : SpecialPowerModuleBase(data.Base)
{
    public override string ModuleName => "CashBountyPower";
    public override bool AcceptsTarget(SpecialPowerTargetKind kind) => false;

    protected override void OnObjectCreated(IWorldServices world)
    {
        var required = world.SpecialPowers[PowerIndex].RequiredScience;
        if (required < 0 || world.PlayerSciences(Entity.Owner).Has(required)) world.RaiseCashBounty(Entity.Owner, data.Bounty);
    }

    public override void OnSpecialPowerCreation(IWorldServices world)
    {
        base.OnSpecialPowerCreation(world);
        world.RaiseCashBounty(Entity.Owner, data.Bounty);
    }
}

// ============================================================================================================ CashHackSpecialPower

public sealed record CashHackData(SpecialPowerBaseData Base, long MoneyAmount, ImmutableArray<(string Science, string Amount)> UpgradeAmounts) : ModuleData
{
    public static CashHackData Parse(ModuleConfiguration configuration) => new(
        SpecialPowerBaseData.Parse(configuration),
        ModuleConfigReader.Int(configuration, "MoneyAmount", 0),
        ModuleConfigReader.Pairs(configuration, "UpgradeMoneyAmount"));
}

/// <summary>
/// <c>CashHackSpecialPower.cpp</c>: steals <c>min(amount, victim's money)</c> from the target object's owner. The amount is the first
/// <c>UpgradeMoneyAmount</c> whose science the thief owns, else <c>MoneyAmount</c>. A location use does nothing, as in the reference.
/// </summary>
public sealed class CashHackSpecialPower(CashHackData data) : SpecialPowerModuleBase(data.Base)
{
    public override string ModuleName => "CashHackSpecialPower";
    public override bool AcceptsTarget(SpecialPowerTargetKind kind) => kind == SpecialPowerTargetKind.Object;

    protected override void Apply(IWorldServices world, in SpecialPowerTarget target)
    {
        var victim = target.Kind == SpecialPowerTargetKind.Object ? world.FindEntity(target.Entity) : null;
        if (victim is null) return;
        var stolen = world.WithdrawMoney(victim.Owner, Fixed.FromInt(AmountToSteal(world)));
        if (stolen > Fixed.Zero) world.AddMoney(Entity.Owner, stolen);
    }

    private long AmountToSteal(IWorldServices world)
    {
        var sciences = world.PlayerSciences(Entity.Owner);
        foreach (var (science, amount) in data.UpgradeAmounts)
        {
            if (!world.Sciences.TryGet(science, out var info)) throw new InvalidOperationException($"CashHackSpecialPower: '{science}' is not a Science definition.");
            if (sciences.Has(info.Index)) return long.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        }
        return data.MoneyAmount;
    }
}

// ============================================================================================================ create modules

/// <summary>
/// Base of the create modules whose work is the reference's <c>onBuildComplete</c>: it runs once, after every module of the object has been
/// constructed and the object exists in the world (one tick after the spawn), and never for an object that is still <c>UNDER_CONSTRUCTION</c>
/// (the simulation has no construction, so such an object never completes). The reference calls <c>onBuildComplete</c> for pre-placed,
/// produced and script-created objects alike; here every spawned, completed object gets it.
/// </summary>
public abstract class BuildCompleteModule : SimModule, IUpdateModule
{
    private long _nextWake = -1;

    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
        => _nextWake = Entity.HasStatus(ObjectStatusBits.UnderConstruction) ? SimulationTiming.SleepForever : world.Tick + SimulationTiming.SleepNone;

    public long Update(IWorldServices world)
    {
        OnBuildComplete(world);
        return SimulationTiming.SleepForever;
    }

    protected abstract void OnBuildComplete(IWorldServices world);

    public override void Describe(IStateWriter writer) => writer.Int("nextWake", _nextWake);
    public override void Load(IStateReader reader) => _nextWake = reader.Int("nextWake");
}

/// <summary><c>SpecialPowerCreate::onBuildComplete</c>: tells every special power of the object that it has come into being (<see cref="ISpecialPowerModule.OnSpecialPowerCreation"/>).</summary>
public sealed class SpecialPowerCreate : BuildCompleteModule
{
    public override string ModuleName => "SpecialPowerCreate";

    protected override void OnBuildComplete(IWorldServices world)
    {
        foreach (var module in Entity.Modules)
            if (module is ISpecialPowerModule power) power.OnSpecialPowerCreation(world);
    }
}

public sealed record GrantUpgradeCreateData(string UpgradeName) : ModuleData
{
    public static GrantUpgradeCreateData Parse(ModuleConfiguration configuration)
    {
        var name = ModuleConfigReader.Text(configuration, "UpgradeToGrant");
        return name.Length > 0 ? new(name) : throw new InvalidDataException("GrantUpgradeCreate names no UpgradeToGrant.");
    }
}

/// <summary>
/// <c>GrantUpgradeCreate.cpp</c>: completes the upgrade for the owner (player-scoped) or the object (object-scoped). The reference grants in
/// <c>onCreate</c> when <c>ExemptStatus</c> lists <c>UNDER_CONSTRUCTION</c> and the object is not under construction, and again in
/// <c>onBuildComplete</c>; both complete the same upgrade, so the module completes it once when the object is complete.
/// </summary>
public sealed class GrantUpgradeCreate(GrantUpgradeCreateData data) : BuildCompleteModule
{
    public override string ModuleName => "GrantUpgradeCreate";

    protected override void OnBuildComplete(IWorldServices world)
    {
        if (!world.Upgrades.TryGet(data.UpgradeName, out var upgrade))
            throw new InvalidOperationException($"GrantUpgradeCreate on '{Entity.Template.Name}': '{data.UpgradeName}' is not an Upgrade definition.");
        world.CompleteUpgrade(Entity.Owner, upgrade.Scope == Templates.UpgradeScope.Object ? Entity.Id : null, data.UpgradeName);
    }
}

// ============================================================================================================ upgrade and die modules

public sealed record GrantScienceUpgradeData(UpgradeMuxData Mux, string Science) : ModuleData
{
    public static GrantScienceUpgradeData Parse(ModuleConfiguration configuration, Templates.UpgradeCatalog catalog)
        => new(UpgradeMuxData.Parse(configuration, catalog), ModuleConfigReader.Text(configuration, "GrantScience"));
}

/// <summary><c>GrantScienceUpgrade::upgradeImplementation</c>: grants the science to the owner (<c>Player::grantScience</c>, which refuses a science that is not <c>IsGrantable</c>).</summary>
public sealed class GrantScienceUpgrade(GrantScienceUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "GrantScienceUpgrade";
    protected override void Activate(IWorldServices world) => world.GrantScience(Entity.Owner, data.Science);
}

public sealed record UnpauseSpecialPowerUpgradeData(UpgradeMuxData Mux, string PowerName) : ModuleData
{
    public static UnpauseSpecialPowerUpgradeData Parse(ModuleConfiguration configuration, Templates.UpgradeCatalog catalog)
        => new(UpgradeMuxData.Parse(configuration, catalog), SpecialPowerModules.RequiredPower(configuration));
}

/// <summary><c>UnpauseSpecialPowerUpgrade::upgradeImplementation</c>: releases one pause of the object's module for the named power.</summary>
public sealed class UnpauseSpecialPowerUpgrade(UnpauseSpecialPowerUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "UnpauseSpecialPowerUpgrade";

    protected override void Activate(IWorldServices world)
    {
        if (!world.SpecialPowers.TryGet(data.PowerName, out var info))
            throw new InvalidOperationException($"UnpauseSpecialPowerUpgrade: '{data.PowerName}' is not a SpecialPower definition.");
        SpecialPowerRouting.FindModule(Entity, info.Index)?.PauseCountdown(world, false);
    }
}

public sealed record SpecialPowerCompletionDieData(DieMuxData Mux, string PowerName) : DieData(Mux)
{
    public static SpecialPowerCompletionDieData Parse(ModuleConfiguration configuration)
        => new(DieMuxData.Parse(configuration), SpecialPowerModules.RequiredPower(configuration));
}

/// <summary>
/// <c>SpecialPowerCompletionDie</c> (<c>SpecialPowerCompletionDie.cpp</c>): tells the scripting engine that a special power's carrier died
/// (<c>notifyScriptEngine</c> -> <c>notifyOfCompletedSpecialPower(owner, power, creator)</c>), as a <see cref="SimEventKind.SpecialPowerCompleted"/> event the script engine
/// reads. Only a carrier with a creator (<c>setCreator</c>, set by the delivery that made it; the first transport and payload of a formation only) reports.
/// </summary>
public sealed class SpecialPowerCompletionDie(SpecialPowerCompletionDieData data) : DieModuleBase(data.Mux)
{
    private EntityId _creator;

    public override string ModuleName => "SpecialPowerCompletionDie";
    public string PowerName => data.PowerName;

    /// <summary><c>SpecialPowerCompletionDie::setCreator</c>.</summary>
    public void SetCreator(EntityId creator) => _creator = creator;

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!_creator.IsValid) return;
        world.Emit(SimEventKind.SpecialPowerCompleted, Entity.Id, _creator, data.PowerName, Entity.Owner.Value);
    }

    public override void Describe(IStateWriter writer)
    {
        if (_creator.IsValid) writer.UInt("creator", _creator.Value);
    }

    public override void Load(IStateReader reader) => _creator = reader.Has("creator") ? new EntityId(checked((uint)reader.UInt("creator"))) : EntityId.Invalid;
}

// ============================================================================================================ SpectreGunshipDeploymentUpdate

public sealed record SpectreGunshipData(string PowerName, string RequiredScience, string GunshipTemplate, int CreateLocation) : ModuleData
{
    public static SpectreGunshipData Parse(ModuleConfiguration configuration)
    {
        var location = ModuleConfigReader.Text(configuration, "CreateLocation");
        return new(
            SpecialPowerModules.RequiredPower(configuration),
            ModuleConfigReader.Text(configuration, "RequiredScience"),
            ModuleConfigReader.Text(configuration, "GunshipTemplateName"),
            // Module data ctor: CREATE_GUNSHIP_AT_EDGE_FARTHEST_FROM_TARGET.
            location.Length == 0 ? 3 : NameTablesPhase5.GunshipCreateLocation.TryIndexOf(location, out var index) ? index : throw new InvalidDataException($"Unknown gunship create location '{location}'."));
    }
}

/// <summary>
/// <c>SpectreGunshipDeploymentUpdate.cpp</c>: the handler of the gunship power (<c>doesSpecialPowerUpdatePassScienceTest</c> = the owner has
/// <c>RequiredScience</c>; the first matching module of the object serves the use, so the highest level listed first wins). The gunship is a
/// flying unit that enters from a map edge and circles the target under its own AI; neither the terrain edge nor that AI exists, so the use
/// reports <see cref="CreationSkipReasons.NoTerrainEdge"/> and only does what the module does for the reference's power state: it starts the
/// recharge of the carrier's power (<c>UpdateModuleStartsAttack</c> leaves that to this module).
/// </summary>
public sealed class SpectreGunshipDeploymentUpdate(SpectreGunshipData data) : SimModule, ISpecialPowerHandler
{
    /// <summary>A gunship that was asked for this tick and does not exist yet: where it flies to (the entity appears at the end of the tick, <see cref="SpectreGunshipUpdate.OnSpawn"/> collects this).</summary>
    private sealed record PendingGunship(int Template, Fixed TargetX, Fixed TargetY, long Tick);

    private readonly List<PendingGunship> _pending = [];

    public override string ModuleName => "SpectreGunshipDeploymentUpdate";

    public bool Handles(IWorldServices world, int powerIndex)
    {
        if (!world.SpecialPowers.TryGet(data.PowerName, out var info) || info.Index != powerIndex) return false;
        if (data.RequiredScience.Length == 0) return true;
        if (!world.Sciences.TryGet(data.RequiredScience, out var science)) throw new InvalidOperationException($"SpectreGunshipDeploymentUpdate: '{data.RequiredScience}' is not a Science definition.");
        return world.PlayerSciences(Entity.Owner).Has(science.Index);
    }

    /// <summary>
    /// <c>initiateIntentToDoSpecialPower</c>: with a terrain (the map's edge) and an instantiable gunship template (<c>GunshipTemplateName</c>) the gunship is created at the edge point
    /// <c>CreateLocation</c> chooses (<c>findClosestEdgePoint</c>/<c>findFarthestEdgePoint</c>), pushed back along the line to the target by the gunship's orbit radius (the reference reads an
    /// uninitialised field here; the gunship's own <c>GunshipOrbitRadius</c> is used), at its locomotor's preferred height, facing the target; its <see cref="SpectreGunshipUpdate"/> takes the
    /// target over when it appears. An earlier gunship of this carrier is let go (it flies on). Without a terrain or a usable template the use reports why it created nothing. The
    /// carrier's power recharges either way.
    /// </summary>
    public void Initiate(IWorldServices world, int powerIndex, in SpecialPowerTarget target)
    {
        var targetX = target.X;
        var targetY = target.Y;
        if (target.Kind == SpecialPowerTargetKind.Object && world.FindEntity(target.Entity) is { } victim) (targetX, targetY) = (victim.X, victim.Y);
        if (world.Terrain is null)
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, NameTablesPhase5.GunshipCreateLocation.Canonical(data.CreateLocation), CreationSkipReasons.NoTerrainEdge);
        else if (!world.Templates.TryGet(data.GunshipTemplate, out var gunship))
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.GunshipTemplate, CreationSkipReasons.UnknownTemplate);
        else if (!gunship.IsInstantiable)
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.GunshipTemplate, CreationSkipReasons.TemplateNotInstantiable);
        else CreateGunship(world, gunship, targetX, targetY);
        SpecialPowerRouting.FindModule(Entity, powerIndex)?.Trigger(world);
    }

    private void CreateGunship(IWorldServices world, ObjectTemplate gunship, Fixed targetX, Fixed targetY)
    {
        var terrain = world.Terrain!;
        // CREATE_AT_EDGE_NEAR_SOURCE, ..._FARTHEST_FROM_SOURCE, ..._NEAR_TARGET, ..._FARTHEST_FROM_TARGET (the module's default)
        var (fromX, fromY) = data.CreateLocation is 0 or 1 ? (Entity.X, Entity.Y) : (targetX, targetY);
        var (edgeX, edgeY) = data.CreateLocation is 0 or 2
            ? FlightModules.ClosestEdgePoint(world, fromX, fromY)
            : (fromX < terrain.ExtentX / Fixed.FromInt(2) ? terrain.ExtentX : Fixed.Zero, fromY < terrain.ExtentY / Fixed.FromInt(2) ? terrain.ExtentY : Fixed.Zero);
        var orbit = gunship.Modules.FirstOrDefault(module => module.Data is SpectreData)?.Data is SpectreData spectre ? spectre.GunshipOrbitRadius : Fixed.Zero;
        // "THE POINT FURTHER OFF THE MAP SO WE CANT SEE THE LAME HOVER AND ACCELLERATE BEHAVIOR"
        var dx = targetX - edgeX;
        var dy = targetY - edgeY;
        var distance = Fixed.Sqrt(dx * dx + dy * dy);
        var (x, y) = distance > Fixed.Zero ? (targetX - dx / distance * (distance + orbit), targetY - dy / distance * (distance + orbit)) : (edgeX, edgeY);
        var angle = FixedTrig.Atan2(targetY - y, targetX - x);
        world.RequestSpawn(gunship.Index, Entity.Owner, x, y, gunship.Locomotor?.PreferredHeight ?? Fixed.Zero, angle);
        _pending.Add(new PendingGunship(gunship.Index, targetX, targetY, world.Tick));
    }

    /// <summary>
    /// The new gunship asks its carrier (same owner) for the target it was created for (<c>spModule->doSpecialPowerAtLocation</c> right after creating it in the reference); false when it
    /// was not created by a deployment.
    /// </summary>
    internal static bool TryTakePending(IWorldServices world, Entity gunship, out Fixed targetX, out Fixed targetY)
    {
        (targetX, targetY) = (Fixed.Zero, Fixed.Zero);
        foreach (var candidate in world.Entities.Where(value => value.Owner == gunship.Owner).OrderBy(value => value.Id.Value))
            foreach (var module in candidate.ModuleArray)
            {
                if (module is not SpectreGunshipDeploymentUpdate deployment) continue;
                var found = deployment._pending.Find(value => value.Template == gunship.Template.Index && world.Tick - value.Tick <= 2);
                if (found is null) continue;
                deployment._pending.Remove(found);
                (targetX, targetY) = (found.TargetX, found.TargetY);
                return true;
            }
        return false;
    }

    public override void Describe(IStateWriter writer)
    {
        if (_pending.Count == 0) return;
        writer.BeginArray("pendingGunships");
        foreach (var entry in _pending)
        {
            writer.BeginObject(null);
            writer.Int("template", entry.Template);
            writer.Fixed("x", entry.TargetX);
            writer.Fixed("y", entry.TargetY);
            writer.Int("tick", entry.Tick);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _pending.Clear();
        if (!reader.Has("pendingGunships")) return;
        foreach (var item in reader.Array("pendingGunships")) _pending.Add(new PendingGunship(checked((int)item.Int("template")), item.Fixed("x"), item.Fixed("y"), item.Int("tick")));
    }
}

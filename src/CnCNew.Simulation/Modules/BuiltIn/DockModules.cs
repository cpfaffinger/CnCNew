using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The supply-economy dock family: the <c>DockUpdate</c> base (approach slots, clearance, the single active docker),
/// <c>SupplyCenterDockUpdate</c>, <c>SupplyWarehouseDockUpdate</c>, the two no-op create modules and <c>AutoDepositUpdate</c>.
/// Sources: <c>DockUpdate.cpp</c>, <c>SupplyCenterDockUpdate.cpp</c>, <c>SupplyWarehouseDockUpdate.cpp</c>,
/// <c>SupplyCenterCreate.cpp</c>, <c>SupplyWarehouseCreate.cpp</c>, <c>AutoDepositUpdate.cpp</c>; the rules and every simplification
/// are listed in docs/GAME-RULES.md (section 6e).
/// </summary>
internal static class DockModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<SupplyCenterDockData>("SupplyCenterDockUpdate", ModuleOwnerKind.Object, (configuration, _) => SupplyCenterDockData.Parse(configuration), data => new SupplyCenterDockUpdate(data)),
        new ModuleBinding<SupplyWarehouseDockData>("SupplyWarehouseDockUpdate", ModuleOwnerKind.Object, (configuration, _) => SupplyWarehouseDockData.Parse(configuration), data => new SupplyWarehouseDockUpdate(data)),
        new ModuleBinding<CreateNoOpData>("SupplyCenterCreate", ModuleOwnerKind.Object, (_, _) => CreateNoOpData.Instance, _ => new SupplyCenterCreate()),
        new ModuleBinding<CreateNoOpData>("SupplyWarehouseCreate", ModuleOwnerKind.Object, (_, _) => CreateNoOpData.Instance, _ => new SupplyWarehouseCreate()),
        new ModuleBinding<AutoDepositData>("AutoDepositUpdate", ModuleOwnerKind.Object, AutoDepositData.Parse, data => new AutoDepositUpdate(data)),
    ];

    /// <summary>The object's dock module (<c>Object::findUpdateModule</c> for a <c>DockUpdateInterface</c>); null when it has none.</summary>
    public static IDockModule? DockOf(Entity entity)
    {
        foreach (var module in entity.ModuleArray)
            if (module is IDockModule dock) return dock;
        return null;
    }
}

/// <summary>What a docker (and the dock machine) may ask of a dock (<c>DockUpdateInterface</c>).</summary>
internal interface IDockModule
{
    /// <summary><c>isClearToApproach</c>: a free approach slot exists (or the docker already owns one; a dynamic dock always has room).</summary>
    bool IsClearToApproach(EntityId docker);

    /// <summary><c>reserveApproachPosition</c>: claims (or re-reads) the docker's slot and reports where to wait.</summary>
    bool ReserveApproachPosition(IWorldServices world, Entity docker, out int index, out Fixed x, out Fixed y);

    /// <summary><c>advanceApproachPosition</c>: moves the docker one slot closer if that slot is free.</summary>
    bool AdvanceApproachPosition(IWorldServices world, Entity docker, ref int index, out Fixed x, out Fixed y);

    bool IsClearToEnter(IWorldServices world, EntityId docker);
    bool IsClearToAdvance(EntityId docker, int index);
    void OnApproachReached(EntityId docker);
    void OnEnterReached(EntityId docker);
    void OnExitReached(EntityId docker);
    void CancelDock(EntityId docker);

    /// <summary><c>action</c>: true while the dock wants to be asked again after the action delay.</summary>
    bool Action(IWorldServices world, Entity docker);

    /// <summary>
    /// <c>getEnterPosition</c>, <c>getDockPosition</c>, <c>getExitPosition</c>: where the docker drives into, at and out of the dock - its model's <c>DockStart</c>, <c>DockAction</c>, <c>DockEnd</c>
    /// in the world. <see langword="null"/> when the model has no <c>DockStart</c> ("if I don't have a bone, you are fine where you are"), except that a flyer is recentred over the dock on its
    /// way in.
    /// </summary>
    (Fixed X, Fixed Y)? PathPoint(IWorldServices world, Entity docker, DockPathStage stage);

    /// <summary><c>isAllowPassthroughType</c> (<c>AllowsPassthrough</c>).</summary>
    bool AllowsPassthrough { get; }

    /// <summary><c>isDockOpen</c>: a closed dock refuses every state of the docker's machine (the ferry in transit); open by default.</summary>
    bool IsOpen { get; }

    /// <summary><c>setDockOpen</c>.</summary>
    void SetOpen(IWorldServices world, bool open);

    /// <summary><c>setDockCrippled</c>: a crippled dock accepts approaches but never grants entry (the supply warehouse below the really-damaged edge).</summary>
    void SetDockCrippled(IWorldServices world, bool crippled);
}

/// <summary>The three moves of a docker through its dock (<c>AIDockMoveToEntryState</c>, <c>AIDockMoveToDockState</c>, <c>AIDockMoveToExitState</c>).</summary>
internal enum DockPathStage { Enter, Dock, Exit }

/// <summary>
/// <c>DockUpdate</c> field set. <c>AllowsPassthrough</c> (default yes): the docker ignores the dock as an obstacle on its way into, at and out of it, and does not adjust the dock
/// and exit points (<c>AIDockMoveToEntryState</c>, <c>AIDockMoveToDockState</c>, <c>AIDockMoveToExitState</c>).
/// </summary>
public record DockData(int NumberApproachPositions) : ModuleData
{
    public const int DynamicApproachPositions = -1;
    private const int DefaultDynamicSize = 10;

    public bool AllowsPassthrough { get; init; } = true;

    internal static int ParseApproachPositions(RatedModuleConfiguration configuration)
        => checked((int)ModuleConfigReader.Int(configuration, "NumberApproachPositions", 0));

    internal static bool ParseAllowsPassthrough(RatedModuleConfiguration configuration) => ModuleConfigReader.Bool(configuration, "AllowsPassthrough", true);

    internal int InitialSlots => NumberApproachPositions == DynamicApproachPositions ? DefaultDynamicSize : NumberApproachPositions;
}

public sealed record SupplyCenterDockData(int NumberApproachPositions, long GrantStealthTicks = 0) : DockData(NumberApproachPositions)
{
    public static SupplyCenterDockData Parse(RatedModuleConfiguration configuration)
        => new(ParseApproachPositions(configuration), ModuleConfigReader.Ticks(configuration, "GrantTemporaryStealth", 0)) { AllowsPassthrough = ParseAllowsPassthrough(configuration) };
}

public sealed record SupplyWarehouseDockData(int NumberApproachPositions, int StartingBoxes, bool DeleteWhenEmpty) : DockData(NumberApproachPositions)
{
    public static SupplyWarehouseDockData Parse(RatedModuleConfiguration configuration) => new(
        ParseApproachPositions(configuration),
        checked((int)ModuleConfigReader.Int(configuration, "StartingBoxes", 1)),
        ModuleConfigReader.Bool(configuration, "DeleteWhenEmpty", false)) { AllowsPassthrough = ParseAllowsPassthrough(configuration) };
}

public sealed record CreateNoOpData : ModuleData
{
    public static CreateNoOpData Instance { get; } = new();
}

/// <summary>
/// <c>DockUpdate</c>: the approach queue and the active-docker hand-over. The approach place of a slot is its model's <c>DockWaitingNN</c> bone where the model has them, else the
/// reference's boneless branch of <c>computeApproachPosition</c> (see <see cref="ApproachPosition"/>). <c>update()</c> makes the owner of the first reached slot the active docker while none is active.
/// Slots and the active docker whose entity died or vanished are released (the reference relies on the docker's own machine to do that).
/// The dock sleeps while no slot and no docker is in use and is woken by the first reservation.
/// </summary>
internal abstract class DockUpdateBase(DockData data) : SimModule, IUpdateModule, IDockModule
{
    private long _nextWake = -1;
    private EntityId[] _owners = new EntityId[data.InitialSlots];
    private bool[] _reached = new bool[data.InitialSlots];
    private EntityId _activeDocker;
    private bool _closed;
    private bool _crippled;
    private bool _dockerInside;

    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public EntityId ActiveDocker => _activeDocker;

    /// <summary><c>m_dockerInside</c>: the active docker has reached the entry and has not left through the exit yet.</summary>
    public bool DockerInside => _dockerInside;

    public bool IsOpen => !_closed;
    public bool IsCrippled => _crippled;

    public void SetOpen(IWorldServices world, bool open)
    {
        if (_closed != !open) world.MarkDirty(Entity);
        _closed = !open;
    }

    /// <summary><c>DockUpdate::setDockCrippled</c>: the dock stops granting entry (it still takes approaches); uncrippling wakes it so a docker that waited is let in.</summary>
    public virtual void SetDockCrippled(IWorldServices world, bool crippled)
    {
        _crippled = crippled;
        world.WakeAt(Entity, this, world.Tick);
        world.MarkDirty(Entity);
    }

    private bool Dynamic => data.NumberApproachPositions == DockData.DynamicApproachPositions;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public bool IsClearToApproach(EntityId docker)
    {
        if (Dynamic) return true;
        foreach (var owner in _owners)
            if (!owner.IsValid || owner == docker) return true;
        return false;
    }

    public bool ReserveApproachPosition(IWorldServices world, Entity docker, out int index, out Fixed x, out Fixed y)
    {
        for (var slot = 0; slot < _owners.Length; slot++)
        {
            if (_owners[slot] != docker.Id && _owners[slot].IsValid) continue;
            _owners[slot] = docker.Id;
            return Claimed(world, docker, slot, out index, out x, out y);
        }
        if (Dynamic)
        {
            Array.Resize(ref _owners, _owners.Length + 1);
            Array.Resize(ref _reached, _reached.Length + 1);
            _owners[^1] = docker.Id;
            return Claimed(world, docker, _owners.Length - 1, out index, out x, out y);
        }
        index = -1;
        x = y = Fixed.Zero;
        return false;
    }

    private bool Claimed(IWorldServices world, Entity docker, int slot, out int index, out Fixed x, out Fixed y)
    {
        index = slot;
        (x, y) = ApproachPosition(world, docker, slot);
        world.WakeAt(Entity, this, world.Tick);
        return true;
    }

    public bool AdvanceApproachPosition(IWorldServices world, Entity docker, ref int index, out Fixed x, out Fixed y)
    {
        x = y = Fixed.Zero;
        if (index <= 0 || _owners[index - 1].IsValid) return false;
        _owners[index - 1] = docker.Id;
        _reached[index - 1] = false;
        _owners[index] = EntityId.Invalid;
        _reached[index] = false;
        index--;
        (x, y) = ApproachPosition(world, docker, index);
        return true;
    }

    /// <summary>
    /// The dock bones this dock reads (<c>DockUpdate::loadDockPositions</c>): none for an object that is <c>KINDOF_IGNORE_DOCKING_BONES</c> ("some of the GLASupplyStash assets still have
    /// docking bones in them") or whose model has none.
    /// </summary>
    protected DockBones? Bones(IWorldServices world)
        => UsesBones && !Entity.Template.IsKindOf("IGNORE_DOCKING_BONES") && world.Templates.Geometry.Docks.TryGetValue(Entity.Template.Name, out var bones) ? bones : null;

    /// <summary>A dock that keeps its own approach and moves (the ferry) reads no bones.</summary>
    protected virtual bool UsesBones => true;

    /// <summary>
    /// <c>m_numberApproachPositionBones</c>: how many of the slots have a <c>DockWaitingNN</c> bone - the model's first ones, up to <c>NumberApproachPositions</c>; none for a dynamic dock
    /// ("dynamic means no bones").
    /// </summary>
    private int ApproachBoneCount(DockBones? bones) => Dynamic || bones is null ? 0 : Math.Min(bones.Waiting.Length, data.NumberApproachPositions);

    /// <summary>
    /// <c>Object::convertBonePosToWorldPos</c>: a bone of the dock's model (the zero vector for one the model does not have) in the world, turned with the dock where its frame ends.
    /// </summary>
    protected (Fixed X, Fixed Y) BoneToWorld(IWorldServices world, BonePose? bone)
    {
        var (x, y, _) = Entity.FrameEnd(world.Tick);
        if (bone is not { } pose) return (x, y);
        var heading = Entity.FrameEndAngle(world.Tick);
        var (cos, sin) = (FixedTrig.Cos(heading), FixedTrig.Sin(heading));
        return (x + pose.X * cos - pose.Y * sin, y + pose.X * sin + pose.Y * cos);
    }

    /// <summary>
    /// <c>DockUpdate::computeApproachPosition</c>. With <c>DockWaiting</c> bones the slot's bone in the world (a slot beyond the model's bones takes the zero vector, the dock's centre); without
    /// them the dock's centre pushed <c>majorRadius / 2</c> towards the docker (all three axes in the vector's length). Then the nearest place <c>findPositionAround</c> finds within 100 units
    /// that nothing overlaps - a supply pile's trucks wait at its edge, not on it. A flyer ignores the dock itself. A dock that is itself a mobile blocker overrides it.
    /// </summary>
    protected virtual (Fixed X, Fixed Y) ApproachPosition(IWorldServices world, Entity docker, int slot)
    {
        var flies = CrateModules.UsesAirborneLocomotor(docker);
        var bones = Bones(world);
        var count = ApproachBoneCount(bones);
        if (count > 0)
        {
            var (boneX, boneY) = BoneToWorld(world, slot < count ? bones!.Waiting[slot] : null);
            return world.FindPositionAround(boneX, boneY, Fixed.Zero, FindPositionRadius, docker, flies ? Entity : null, out var atX, out var atY) ? (atX, atY) : (boneX, boneY);
        }
        var (x, y) = (Entity.X, Entity.Y);
        // The docker where its frame ends (the frame convention: what the dock sees of it at 30 Hz).
        var (dockerX, dockerY, dockerZ) = docker.FrameEnd(world.Tick);
        var dx = dockerX - Entity.X;
        var dy = dockerY - Entity.Y;
        var dz = dockerZ - Entity.Z;
        var distSq = dx * dx + dy * dy + dz * dz;
        if (distSq != Fixed.Zero)
        {
            var dist = Fixed.Sqrt(distSq);
            var reach = Entity.Template.GeometryMajorRadius / Fixed.FromInt(2);
            x += dx / dist * reach;
            y += dy / dist * reach;
        }
        return world.FindPositionAround(x, y, Fixed.Zero, FindPositionRadius, docker, flies ? Entity : null, out var foundX, out var foundY) ? (foundX, foundY) : (x, y);
    }

    /// <summary><c>fpOptions.maxRadius</c> of <c>computeApproachPosition</c>.</summary>
    private static readonly Fixed FindPositionRadius = Fixed.FromInt(100);

    public bool AllowsPassthrough => data.AllowsPassthrough;

    public (Fixed X, Fixed Y)? PathPoint(IWorldServices world, Entity docker, DockPathStage stage)
    {
        // DockUpdate tells a boneless dock by its enter position alone (m_enterPosition == zero), for all three.
        if (Bones(world) is not { Start: { } start } bones || (start.X == Fixed.Zero && start.Y == Fixed.Zero && start.Z == Fixed.Zero))
        {
            if (stage != DockPathStage.Enter || !CrateModules.UsesAirborneLocomotor(docker)) return null;
            var (x, y, _) = Entity.FrameEnd(world.Tick);
            return (x, y);
        }
        return BoneToWorld(world, stage switch { DockPathStage.Enter => start, DockPathStage.Dock => bones.Action, _ => bones.End });
    }

    public virtual bool IsClearToEnter(IWorldServices world, EntityId docker) => docker == _activeDocker;

    public bool IsClearToAdvance(EntityId docker, int index)
        => index > 0 && index < _owners.Length && _owners[index] == docker && _reached[index] && !_owners[index - 1].IsValid;

    public void OnApproachReached(EntityId docker)
    {
        var slot = Array.IndexOf(_owners, docker);
        if (slot >= 0) _reached[slot] = true;
    }

    public void OnEnterReached(EntityId docker)
    {
        _dockerInside = true;
        var slot = Array.IndexOf(_owners, docker);
        if (slot < 0) return;
        _owners[slot] = EntityId.Invalid;
        _reached[slot] = false;
    }

    public void OnExitReached(EntityId docker)
    {
        _dockerInside = false;
        if (docker == _activeDocker) _activeDocker = EntityId.Invalid;
    }

    public void CancelDock(EntityId docker)
    {
        for (var slot = 0; slot < _owners.Length; slot++)
        {
            if (_owners[slot] != docker) continue;
            _owners[slot] = EntityId.Invalid;
            _reached[slot] = false;
        }
        if (_activeDocker == docker)
        {
            _activeDocker = EntityId.Invalid;
            _dockerInside = false;
        }
    }

    public abstract bool Action(IWorldServices world, Entity docker);

    public virtual long Update(IWorldServices world)
    {
        var busy = false;
        for (var slot = 0; slot < _owners.Length; slot++)
        {
            if (!_owners[slot].IsValid) continue;
            if (IsGone(world, _owners[slot])) { _owners[slot] = EntityId.Invalid; _reached[slot] = false; }
            else busy = true;
        }
        if (_activeDocker.IsValid && IsGone(world, _activeDocker))
        {
            _activeDocker = EntityId.Invalid;
            _dockerInside = false;
        }
        // DockUpdate::update: "if setDockCrippled has been called, I will never give enterance permission".
        // The hand-over is one step of the reference's update, once per logic frame of the original, and the dockers' machines ask for clearance on the same grid
        // (AIUpdateInterface.UpdateDock, SupplyTruckAIUpdate): a dock that hands over in every tick lets the next docker in a frame before the 30 Hz dock does.
        if (!_activeDocker.IsValid && !_crippled && world.Time.IsOriginalFrameBoundary(world.Tick))
        {
            for (var slot = 0; slot < _owners.Length; slot++)
            {
                if (!_reached[slot]) continue;
                _activeDocker = _owners[slot];
                break;
            }
        }
        return busy || _activeDocker.IsValid ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    private static bool IsGone(IWorldServices world, EntityId id)
        => world.FindEntity(id) is not { } entity || entity.DestroyRequested || entity.EffectivelyDead;

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.UInt("activeDocker", _activeDocker.Value);
        // Written only when set, so every dock that was never closed, crippled or entered keeps its serialized form.
        if (_closed) writer.Bool("closed", true);
        if (_crippled) writer.Bool("crippled", true);
        if (_dockerInside) writer.Bool("dockerInside", true);
        writer.BeginArray("slots");
        for (var slot = 0; slot < _owners.Length; slot++)
        {
            writer.BeginObject(null);
            writer.UInt("owner", _owners[slot].Value);
            writer.Bool("reached", _reached[slot]);
            writer.EndObject();
        }
        writer.EndArray();
        DescribeDock(writer);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _activeDocker = new EntityId(checked((uint)reader.UInt("activeDocker")));
        _closed = reader.Has("closed") && reader.Bool("closed");
        _crippled = reader.Has("crippled") && reader.Bool("crippled");
        _dockerInside = reader.Has("dockerInside") && reader.Bool("dockerInside");
        var slots = reader.Array("slots");
        _owners = new EntityId[slots.Count];
        _reached = new bool[slots.Count];
        for (var slot = 0; slot < slots.Count; slot++)
        {
            _owners[slot] = new EntityId(checked((uint)slots[slot].UInt("owner")));
            _reached[slot] = slots[slot].Bool("reached");
        }
        LoadDock(reader);
    }

    protected virtual void DescribeDock(IStateWriter writer) { }
    protected virtual void LoadDock(IStateReader reader) { }
}

// ================================================================================================= supply center

/// <summary>
/// <c>SupplyCenterDockUpdate::action</c>: the docker unloads every box at <c>ValuePerSupplyBox</c> (<c>Player::getSupplyBoxValue</c>) plus
/// its upgraded boost once, the sum goes to the centre's controlling player, and the action never asks to be repeated. A stealthed centre grants the docker <c>GrantTemporaryStealth</c>.
/// Not modelled: the score keeper and the floating text.
/// </summary>
internal sealed class SupplyCenterDockUpdate(SupplyCenterDockData data) : DockUpdateBase(data)
{
    public override string ModuleName => "SupplyCenterDockUpdate";

    public override bool Action(IWorldServices world, Entity docker)
    {
        if (SupplyNetwork.CarrierOf(docker) is not { } carrier) return false;
        long value = 0;
        // The adopted fix proportional-supply-boost (docs/GGC-FIXES.md): the boost by the boxes on board, read before they are unloaded.
        var proportional = world.Templates.Rules.Has(Rules.GameFix.ProportionalSupplyBoost);
        if (proportional) value += Rules.GameFixRules.ProportionalSupplyBoost(carrier.UpgradedSupplyBoost(world), carrier.Boxes, carrier.MaxBoxes);
        while (carrier.LoseOneBox()) value += world.ValuePerSupplyBox;
        if (!proportional) value += carrier.UpgradedSupplyBoost(world);
        if (value <= 0) return false;
        world.AddMoney(Entity.Owner, Fixed.FromInt(value));
        world.Emit(SimEventKind.SupplyDeposited, docker.Id, Entity.Id, string.Empty, value);
        carrier.NoteDelivery(world, Entity);
        world.MarkDirty(docker);
        if (data.GrantStealthTicks > 0) StealthUpdate.GrantFrom(world, Entity, docker, data.GrantStealthTicks);
        return false;
    }
}

// ============================================================================================= supply warehouse

/// <summary>
/// <c>SupplyWarehouseDockUpdate::action</c>: hands one box to the docker per action while stock remains; the last box of a
/// <c>DeleteWhenEmpty</c> warehouse destroys it. The reference's "close enough" check (the docker twitches by a random offset when it stopped
/// too far away) is skipped: the docker stops at the warehouse's <c>DockAction</c> bone or, at a boneless pile, where it waited beside it. <c>setDockCrippled</c> (raised by
/// <see cref="SupplyWarehouseCripplingBehavior"/>) kills a truck that is between the entry and the exit and sends one that has only approached
/// back to idle with its "want supplies" state forced, before the dock stops granting entry.
/// </summary>
internal sealed class SupplyWarehouseDockUpdate(SupplyWarehouseDockData data) : DockUpdateBase(data)
{
    private int _boxes = data.StartingBoxes;

    public override string ModuleName => "SupplyWarehouseDockUpdate";
    public int Boxes => _boxes;

    /// <summary><c>SupplyWarehouseDockUpdate::setCashValue</c> (<c>WAREHOUSE_SET_VALUE</c>): the stock becomes enough boxes for the cash value, rounded up.</summary>
    public void SetCashValue(IWorldServices world, int cash, int valuePerBox)
    {
        _boxes = valuePerBox > 0 ? (int)((Math.Max(cash, 0) + (long)valuePerBox - 1) / valuePerBox) : 0;
        // MarkDirty, not the flag: the flag alone leaves the entity out of the world's dirty list and makes every later MarkDirty return early.
        world.MarkDirty(Entity);
    }

    public override bool Action(IWorldServices world, Entity docker)
    {
        if (_boxes == 0) return false;
        if (SupplyNetwork.CarrierOf(docker) is not { } carrier) return false;
        _boxes--;
        if (!carrier.GainOneBox(_boxes))
        {
            _boxes++;
            return false;
        }
        if (_boxes == 0) carrier.AnnounceEmptySource(world);
        world.MarkDirty(docker);
        world.MarkDirty(Entity);
        if (_boxes == 0 && data.DeleteWhenEmpty)
        {
            world.RequestDestroy(Entity.Id);
            return false;
        }
        return true;
    }

    public override void SetDockCrippled(IWorldServices world, bool crippled)
    {
        // SupplyWarehouseDockUpdate::setDockCrippled: "At this level, Crippling means I kill any activeDocker between enter and exit."
        if (crippled && ActiveDocker.IsValid && world.FindEntity(ActiveDocker) is { } victim)
        {
            if (DockerInside)
            {
                if (!CrateModules.UsesAirborneLocomotor(victim)) world.Kill(victim.Id);
            }
            else if (SupplyNetwork.CarrierOf(victim) is { } carrier)
            {
                // Between approach and enter: stop, but remind him that he wants to try again later.
                CrateModules.AiOf(victim)?.OrderIdle(world);
                carrier.ForceWanting(world);
            }
        }
        base.SetDockCrippled(world, crippled);
    }

    protected override void DescribeDock(IStateWriter writer) => writer.Int("boxes", _boxes);
    protected override void LoadDock(IStateReader reader) => _boxes = checked((int)reader.Int("boxes"));
}

/// <summary><c>SupplyCenterCreate</c>: registers the centre with its player's <c>ResourceGatheringManager</c>; the network here is derived from the entities, so nothing is left to do.</summary>
internal sealed class SupplyCenterCreate : SimModule
{
    public override string ModuleName => "SupplyCenterCreate";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

/// <summary><c>SupplyWarehouseCreate</c>: see <see cref="SupplyCenterCreate"/>.</summary>
internal sealed class SupplyWarehouseCreate : SimModule
{
    public override string ModuleName => "SupplyWarehouseCreate";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// =================================================================================================== AutoDeposit

/// <summary><c>AutoDepositUpdate.cpp</c> field set.</summary>
public sealed record AutoDepositData(long DepositTimingTicks, int DepositAmount, bool ActualMoney, ImmutableArray<(string Upgrade, int Boost)> UpgradedBoosts, int InitialCaptureBonus) : ModuleData
{
    public static AutoDepositData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades)
    {
        var boosts = ImmutableArray.CreateBuilder<(string, int)>();
        foreach (var field in ModuleConfigReader.All(configuration, "UpgradedBoost"))
        {
            var (upgrade, boost) = ParseBoost(field.Value.OriginalText);
            if (!upgrades.TryGet(upgrade, out _)) throw new InvalidDataException($"AutoDepositUpdate UpgradedBoost names the unknown upgrade '{upgrade}'.");
            boosts.Add((upgrade, boost));
        }
        return new AutoDepositData(
            ModuleConfigReader.Ticks(configuration, "DepositTiming", 0),
            checked((int)ModuleConfigReader.Int(configuration, "DepositAmount", 0)),
            ModuleConfigReader.Bool(configuration, "ActualMoney", true),
            boosts.ToImmutable(),
            checked((int)ModuleConfigReader.Int(configuration, "InitialCaptureBonus", 0)));
    }

    /// <summary><c>INI::parseUpgradePair</c>: <c>UpgradeType:&lt;name&gt; Boost:&lt;int&gt;</c>.</summary>
    private static (string Upgrade, int Boost) ParseBoost(string text)
    {
        string? upgrade = null;
        int? boost = null;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = token.IndexOf(':');
            if (colon <= 0) throw new InvalidDataException($"AutoDepositUpdate UpgradedBoost token '{token}' is not 'Key:Value'.");
            var key = token[..colon];
            var value = token[(colon + 1)..];
            if (key.Equals("UpgradeType", StringComparison.OrdinalIgnoreCase)) upgrade = value;
            else if (key.Equals("Boost", StringComparison.OrdinalIgnoreCase)) boost = int.Parse(value, CultureInfo.InvariantCulture);
            else throw new InvalidDataException($"AutoDepositUpdate UpgradedBoost has the unknown key '{key}'.");
        }
        return upgrade is not null && boost is { } amount ? (upgrade, amount) : throw new InvalidDataException($"AutoDepositUpdate UpgradedBoost '{text}' needs UpgradeType and Boost.");
    }
}

/// <summary>
/// <c>AutoDepositUpdate</c>: every <c>DepositTiming</c> ticks the controlling player receives <c>DepositAmount</c> plus the first boost whose
/// upgrade the player owns. Neutral objects and objects still under construction earn nothing (the timer still advances); with
/// <c>ActualMoney = No</c> the reference only shows the number, which is not modelled. The first player who takes it after its first deposit date is paid <c>InitialCaptureBonus</c>, once
/// (<c>awardInitialCaptureBonus</c>, from <c>Player::becomingTeamMember</c>); "we don't want to award initial bonus on load", so a map's first owner has none.
/// </summary>
internal sealed class AutoDepositUpdate(AutoDepositData data) : SimModule, IUpdateModule, IOwnerChangeReceiver
{
    private long _nextWake = -1;
    private long _depositOnTick;
    private bool _bonusStanding;
    private bool _initialized;

    public override string ModuleName => "AutoDepositUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        _depositOnTick = world.Tick + data.DepositTimingTicks;
        _nextWake = Math.Max(_depositOnTick, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        if (world.Tick < _depositOnTick) return _depositOnTick - world.Tick;
        if (!_initialized)
        {
            _bonusStanding = true;
            _initialized = true;
        }
        // A DepositTiming of 0 pays once per frame of the original, not once per tick (the income per second must not depend on the rate).
        _depositOnTick = world.Tick + world.Time.AtLeastOneFrame(data.DepositTimingTicks);
        if (!Entity.Owner.IsSystem && data.DepositAmount > 0 && !Entity.HasStatus(ObjectStatusBits.UnderConstruction) && data.ActualMoney)
        {
            var money = data.DepositAmount + Boost(world);
            world.AddMoney(Entity.Owner, Fixed.FromInt(money));
            world.Emit(SimEventKind.SupplyDeposited, Entity.Id, Entity.Id, string.Empty, money);
        }
        return _depositOnTick - world.Tick;
    }

    /// <inheritdoc/>
    public void OnOwnerChanged(IWorldServices world, PlayerId oldOwner, PlayerId newOwner)
    {
        if (newOwner.IsSystem) return;
        _depositOnTick = world.Tick + data.DepositTimingTicks;
        world.WakeAt(Entity, this, _depositOnTick);
        world.MarkDirty(Entity);
        if (!_bonusStanding || data.InitialCaptureBonus <= 0) return;
        world.AddMoney(newOwner, Fixed.FromInt(data.InitialCaptureBonus));
        world.Emit(SimEventKind.SupplyDeposited, Entity.Id, Entity.Id, string.Empty, data.InitialCaptureBonus);
        _bonusStanding = false;
    }

    private int Boost(IWorldServices world)
    {
        var owned = world.PlayerUpgrades(Entity.Owner);
        foreach (var (upgrade, boost) in data.UpgradedBoosts)
            if (world.Upgrades.TryGet(upgrade, out var info) && owned.Has(info.Index)) return boost;
        return 0;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("depositOnTick", _depositOnTick);
        if (_bonusStanding) writer.Bool("bonusStanding", true);
        if (_initialized) writer.Bool("initialized", true);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _depositOnTick = reader.Int("depositOnTick");
        _bonusStanding = reader.Has("bonusStanding");
        _initialized = reader.Has("initialized");
    }
}

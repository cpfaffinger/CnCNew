using System.Collections.Immutable;
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
/// The structures-and-world module group, first half (<c>docs/SIMULATION-CORE.md</c> §29): <c>UpgradeDie</c>, <c>StructureCollapseUpdate</c>, <c>FireWeaponCollide</c>,
/// <c>FireSpreadUpdate</c>, <c>DeployStyleAIUpdate</c> and <c>FloatUpdate</c>. Each cites its reference file; what a module leaves to presentation is stated at the module.
/// </summary>
internal static class StructureModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<UpgradeDieData>("UpgradeDie", ModuleOwnerKind.Object, (configuration, _) => UpgradeDieData.Parse(configuration), data => new UpgradeDie(data)),
        new ModuleBinding<StructureCollapseData>("StructureCollapseUpdate", ModuleOwnerKind.Object, (configuration, _) => StructureCollapseData.Parse(configuration), data => new StructureCollapseUpdate(data)),
        new ModuleBinding<FireWeaponCollideData>("FireWeaponCollide", ModuleOwnerKind.Object, (configuration, _) => FireWeaponCollideData.Parse(configuration), data => new FireWeaponCollide(data)),
        new ModuleBinding<FireSpreadData>("FireSpreadUpdate", ModuleOwnerKind.Object, (configuration, _) => FireSpreadData.Parse(configuration), data => new FireSpreadUpdate(data)),
        new ModuleBinding<DeployStyleData>("DeployStyleAIUpdate", ModuleOwnerKind.Object, (configuration, _) => DeployStyleData.Parse(configuration), data => new DeployStyleAIUpdate(data)),
        new ModuleBinding<FloatData>("FloatUpdate", ModuleOwnerKind.Object, (configuration, _) => FloatData.Parse(configuration), data => new FloatUpdate(data)),
    ];

    /// <summary><c>GeometryInfo::getMaxHeightAbovePosition</c>: the height of a box or cylinder, the radius of a sphere.</summary>
    public static Fixed MaxHeightAbovePosition(ObjectTemplate template)
        => template.Geometry.Equals("SPHERE", StringComparison.OrdinalIgnoreCase) ? template.GeometryMajorRadius : template.GeometryHeight;

    /// <summary>The tokens of an opaque accumulated value (<c>"INITIAL FX_a FX_b"</c>), unquoted and in order.</summary>
    public static string[] Tokens(DefinitionField field) => field.Value.OriginalText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}

// ================================================================================================================ UpgradeDie

/// <summary><c>UpgradeDie.h</c>: the die filter (<c>DeathTypes</c> and the others) plus <c>UpgradeToRemove</c>. A trailing second token on the line (a stray module tag in the shipped data) is ignored, as the reference's single-token parse does.</summary>
public sealed record UpgradeDieData(DieMuxData Mux, string UpgradeToRemove) : DieData(Mux)
{
    public static UpgradeDieData Parse(RatedModuleConfiguration configuration) => new(DieMuxData.Parse(configuration), ModuleConfigReader.Text(configuration, "UpgradeToRemove"));
}

/// <summary>
/// <c>UpgradeDie::onDie</c>: when the object dies, the object-scoped upgrade <c>UpgradeToRemove</c> is taken away from the object that created it (<c>getProducerID</c>), if that object still
/// exists and has the upgrade — so a destroyed battle/scout/hellfire drone frees its owner's upgrade slot for a new one. The producer is recorded by the creation list that made the
/// object (<see cref="IProducerReceiver"/>); an object created any other way has none and the module does nothing, exactly like a reference object whose producer id is invalid.
/// </summary>
public sealed class UpgradeDie(UpgradeDieData data) : DieModuleBase(data.Mux), IProducerReceiver
{
    private EntityId _producer = EntityId.Invalid;

    public override string ModuleName => "UpgradeDie";
    public EntityId Producer => _producer;

    public void OnProducedBy(IWorldServices world, EntityId producer) => _producer = producer;

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!Mux.IsApplicable(Entity, info) || !_producer.IsValid) return;
        if (world.FindEntity(_producer) is not { } producer || producer.DestroyRequested) return;
        if (!world.Upgrades.TryGet(data.UpgradeToRemove, out var upgrade) || upgrade.Scope != UpgradeScope.Object || !producer.CompletedUpgrades.Has(upgrade.Index)) return;
        world.RemoveUpgradeByName(producer, data.UpgradeToRemove);
    }

    public override void Describe(IStateWriter writer)
    {
        if (_producer.IsValid) writer.UInt("producer", _producer.Value);
    }

    public override void Load(IStateReader reader) => _producer = reader.Has("producer") ? new EntityId(checked((uint)reader.UInt("producer"))) : EntityId.Invalid;
}

// ================================================================================================================ StructureCollapseUpdate

/// <summary>
/// <c>StructureCollapseUpdate.cpp</c>'s fields: the die filter, the delay before the collapse starts and between bursts (ticks), the damping and the burst frequency, and per phase
/// (<c>INITIAL</c>, <c>DELAY</c>, <c>BURST</c>, <c>FINAL</c>) the lists of effect lists and creation lists one is picked from. <c>MaxShudder</c> only shakes the model (presentation, typed).
/// </summary>
public sealed record StructureCollapseData(DieMuxData Mux, long MinCollapseDelay, long MaxCollapseDelay, long MinBurstDelay, long MaxBurstDelay, Fixed Damping, int BigBurstFrequency,
    ImmutableArray<ImmutableArray<string>> OclByPhase, ImmutableArray<ImmutableArray<string>> FxByPhase) : DieData(Mux)
{
    public static readonly string[] PhaseNames = ["INITIAL", "DELAY", "BURST", "FINAL"];

    public static StructureCollapseData Parse(RatedModuleConfiguration configuration) => new(
        DieMuxData.Parse(configuration),
        ModuleConfigReader.Ticks(configuration, "MinCollapseDelay", 0), ModuleConfigReader.Ticks(configuration, "MaxCollapseDelay", 0),
        // The constructor default of the minimum burst delay is 9999 frames and the maximum starts at zero (a data file always sets both).
        ModuleConfigReader.Ticks(configuration, "MinBurstDelay", 9999), ModuleConfigReader.Ticks(configuration, "MaxBurstDelay", 0),
        ModuleConfigReader.Real(configuration, "CollapseDamping", Fixed.Zero), checked((int)ModuleConfigReader.Int(configuration, "BigBurstFrequency", 0)),
        Phased(configuration, "OCL"), Phased(configuration, "FXList"));

    /// <summary><c>parseOCL</c>/<c>parseFX</c>: a phase name (<c>scanIndexList</c>) followed by every name of the line, appended to that phase's list.</summary>
    private static ImmutableArray<ImmutableArray<string>> Phased(RatedModuleConfiguration configuration, string key)
    {
        var lists = new List<string>[PhaseNames.Length];
        for (var phase = 0; phase < lists.Length; phase++) lists[phase] = [];
        foreach (var field in ModuleConfigReader.All(configuration, key))
        {
            var tokens = StructureModules.Tokens(field);
            var phase = tokens.Length == 0 ? -1 : Array.FindIndex(PhaseNames, name => name.Equals(tokens[0], StringComparison.OrdinalIgnoreCase));
            if (phase < 0) throw new InvalidDataException($"{field.Location.Source}({field.Location.Line}): StructureCollapseUpdate {key} needs a phase name (INITIAL, DELAY, BURST, FINAL), got '{field.Value.OriginalText.Trim()}'.");
            foreach (var name in tokens.Skip(1)) lists[phase].Add(name);
        }
        return [..lists.Select(list => list.ToImmutableArray())];
    }
}

/// <summary>
/// <c>StructureCollapseUpdate</c>: when the building dies (<c>onDie</c>, die filter permitting) it waits a random <c>MinCollapseDelay..MaxCollapseDelay</c> (<c>GameLogicRandomValue</c>), then
/// collapses: the visual height drops under <c>COLLAPSE_ACCELERATION</c> by the gravity scaled with <c>1 - CollapseDamping</c> until the whole template height is below the ground, and every
/// random <c>MinBurstDelay..MaxBurstDelay</c> a burst or delay effect goes off (a burst with chance <c>1 / BigBurstFrequency</c>). At the start of each phase one effect list and one creation list of
/// that phase are picked (<c>buildNonDupRandomIndexList</c>: one <c>GameLogicRandomValue</c> draw per non-empty list, creation lists executed at the building's position, effect lists requested
/// as events). The shuddering and the sinking move only the model (<c>setInstanceMatrix</c>), so nothing here moves the entity; the visual height is state and <see cref="IsCollapsed"/> tells
/// presentation when the rubble model takes over (<c>MODELCONDITION_POST_COLLAPSE</c>). Not modeled: deselecting the building and stopping its AI (<c>markAsDead</c>), the bone effects
/// (<c>BoneFXUpdate::stopAllBoneFX</c>; that module is a shell here).
/// </summary>
public sealed class StructureCollapseUpdate(StructureCollapseData data) : SimModule, IUpdateModule, IDieModule
{
    private enum State : byte { Standing, WaitingForCollapseStart, Collapsing, Done }

    private long _nextWake = SimulationTiming.SleepForever;
    private State _state;
    private long _collapseTick;
    private long _burstTick;
    private Fixed _velocity;
    private Fixed _height;
    // The sinking is one step of the original per logic frame of the original (height -= velocity, then velocity -= gravity), in its units: the velocity is
    // a distance per FRAME at every rate, the height covers the frame's step tick by tick, gravity, burst and the end conclude the frame on its last tick.
    // At rest (unwritten) between two frames, which is always at 30 Hz.
    private FramePhase _framePhase;

    public override string ModuleName => "StructureCollapseUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool IsCollapsed => _state == State.Done;
    public bool IsCollapsing => _state is State.WaitingForCollapseStart or State.Collapsing;
    public Fixed VisualHeight => _height;

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!data.Mux.IsApplicable(Entity, info) || _state != State.Standing) return;
        _collapseTick = world.Tick + world.Time.DrawFrames(world.LogicRandom, data.MinCollapseDelay, data.MaxCollapseDelay);
        DoPhase(world, 0, Entity.X, Entity.Y, Entity.Z);
        _state = State.WaitingForCollapseStart;
        _height = Fixed.Zero;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        if (_state == State.Standing) return SimulationTiming.SleepForever;
        var now = world.Tick;
        var startsNow = false;
        if (_state == State.WaitingForCollapseStart && now >= _collapseTick)
        {
            _state = State.Collapsing;
            DoPhase(world, 2, Entity.X, Entity.Y, Entity.Z);
            _burstTick = now + world.Time.DrawFrames(world.LogicRandom, data.MinBurstDelay, data.MaxBurstDelay);
            _framePhase.Reset();
            startsNow = true;
        }
        if (_state == State.Collapsing)
        {
            // The original takes its first step in the update that starts the collapse: that tick ends a frame of the collapse (nothing has moved in it, the
            // velocity is still zero), and every later frame ends a whole frame of the original after it.
            if (startsNow) _height -= _velocity;
            else
            {
                _height -= world.Time.FrameShare(_velocity, _framePhase.Next(world.Time));
                if (!_framePhase.AtBoundary) return SimulationTiming.SleepNone;
            }
            _velocity -= world.Time.PerTickSquaredToPerOriginalFrameSquared(world.Templates.GravityPerTickSquared) * (Fixed.One - data.Damping);
            if (now >= _burstTick)
            {
                // GameLogicRandomValue(1, BigBurstFrequency) == 1: a big burst, otherwise the smaller delay effect.
                DoPhase(world, world.LogicRandom.Range(1, data.BigBurstFrequency) == 1 ? 2 : 1, Entity.X, Entity.Y, Entity.Z);
                _burstTick += world.Time.DrawFrames(world.LogicRandom, data.MinBurstDelay, data.MaxBurstDelay);
            }
            if (_height + StructureModules.MaxHeightAbovePosition(Entity.Template) <= Fixed.Zero)
            {
                _state = State.Done;
                DoPhase(world, 3, Entity.X, Entity.Y, Entity.Z);
                return SimulationTiming.SleepForever;
            }
        }
        return SimulationTiming.SleepNone;
    }

    /// <summary><c>doPhaseStuff</c>: one effect list (event) and one creation list of the phase, each picked with one logic-stream draw when the phase has any.</summary>
    private void DoPhase(IWorldServices world, int phase, Fixed x, Fixed y, Fixed z)
    {
        var effects = data.FxByPhase[phase];
        if (effects.Length > 0)
        {
            var name = effects[world.LogicRandom.Range(0, effects.Length - 1)];
            if (name.Length > 0 && !name.Equals("None", StringComparison.OrdinalIgnoreCase)) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, name, 0, x, y, z);
        }
        var lists = data.OclByPhase[phase];
        if (lists.Length > 0)
        {
            var name = lists[world.LogicRandom.Range(0, lists.Length - 1)];
            if (name.Length > 0 && !name.Equals("None", StringComparison.OrdinalIgnoreCase)) world.CreateFromList(Entity, name, EntityId.Invalid, false, x, y, z);
        }
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (_state == State.Standing) return;
        writer.Int("state", (int)_state);
        writer.Int("collapseTick", _collapseTick);
        writer.Int("burstTick", _burstTick);
        writer.Fixed("velocity", _velocity);
        writer.Fixed("height", _height);
        _framePhase.Describe(writer, "framePhase");
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        if (!reader.Has("state")) return;
        var state = reader.Int("state");
        if (state < 0 || state > (int)State.Done) throw new InvalidDataException("Structure collapse state out of range.");
        _state = (State)state;
        _collapseTick = reader.Int("collapseTick");
        _burstTick = reader.Int("burstTick");
        _velocity = reader.Fixed("velocity");
        _height = reader.Fixed("height");
        _framePhase.Load(reader, "framePhase");
    }
}

// ================================================================================================================ FireWeaponCollide

/// <summary><c>FireWeaponCollide.cpp</c>'s fields: the weapon, <c>FireOnce</c>, and the object statuses that must all be set (<c>RequiredStatus</c>) or none be set (<c>ForbiddenStatus</c>).</summary>
public sealed record FireWeaponCollideData(string Weapon, bool FireOnce, ulong RequiredStatus, ulong ForbiddenStatus) : ModuleData
{
    public static FireWeaponCollideData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Text(configuration, "CollideWeapon"), ModuleConfigReader.Bool(configuration, "FireOnce", false),
        ObjectStatusBits.Mask(ModuleConfigReader.Flags(configuration, "RequiredStatus"), NameTables.ObjectStatus),
        ObjectStatusBits.Mask(ModuleConfigReader.Flags(configuration, "ForbiddenStatus"), NameTables.ObjectStatus));
}

/// <summary>
/// <c>FireWeaponCollide::onCollide</c>: whenever something touches the object while it has every <c>RequiredStatus</c> and none of the <c>ForbiddenStatus</c> bits (a tree that is
/// <c>AFLAME</c>), the collide weapon is reloaded and fired at the toucher — every time, as the reference's own comment says, so that all that touch it are hurt. <c>FireOnce</c> is typed but,
/// like in the reference (<c>m_everFired</c> is read and never set), has no effect. A weapon the catalog does not know fires nothing.
/// </summary>
public sealed class FireWeaponCollide(FireWeaponCollideData data) : SimModule, ICollideModule
{
    public override string ModuleName => "FireWeaponCollide";

    public void OnCollide(IWorldServices world, Entity other)
    {
        if (other.DestroyRequested || (Entity.Status & data.RequiredStatus) != data.RequiredStatus || (Entity.Status & data.ForbiddenStatus) != 0) return;
        if (data.Weapon.Length == 0 || !world.Weapons.TryGet(data.Weapon, out var weapon)) return;
        world.FireWeaponAt(Entity, weapon, other, other.X, other.Y, other.Z);
    }

    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ================================================================================================================ FireSpreadUpdate

/// <summary><c>FireSpreadUpdate.cpp</c>'s fields: the ember creation list, the spread delay range (ticks) and how far a fire can jump (zero: only embers).</summary>
public sealed record FireSpreadData(string OclEmbers, long MinDelay, long MaxDelay, Fixed Range) : ModuleData
{
    public static FireSpreadData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Text(configuration, "OCLEmbers"), ModuleConfigReader.Ticks(configuration, "MinSpreadDelay", 0), ModuleConfigReader.Ticks(configuration, "MaxSpreadDelay", 0),
        ModuleConfigReader.Real(configuration, "SpreadTryRange", Fixed.Zero));
}

/// <summary>
/// <c>FireSpreadUpdate</c>: asleep until <see cref="FlammableUpdate"/> sets the object alight (<c>startFireSpreading</c>); while it is <c>AFLAME</c> it creates its embers
/// (<c>OCLEmbers</c>) and, when <c>SpreadTryRange</c> is set, sets the closest object within that range (centre to centre, in three dimensions) that could still catch fire alight, then
/// sleeps a random <c>MinSpreadDelay..MaxSpreadDelay</c> (at least one tick) before trying again. Once the object no longer burns the module sleeps for good until it ignites again.
/// </summary>
public sealed class FireSpreadUpdate(FireSpreadData data) : SimModule, IUpdateModule
{
    private static readonly int AflameBit = ObjectStatusBits.Index("AFLAME");
    private long _nextWake = SimulationTiming.SleepForever;

    public override string ModuleName => "FireSpreadUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary><c>FireSpreadUpdate::startFireSpreading</c>: only an object that is on fire starts the countdown.</summary>
    public void StartFireSpreading(IWorldServices world)
    {
        if (!Entity.HasStatus(AflameBit)) return;
        world.WakeAt(Entity, this, world.Tick + NextDelay(world));
    }

    private long NextDelay(IWorldServices world) => world.Time.AtLeastOneFrame(world.Time.DrawFrames(world.LogicRandom, data.MinDelay, data.MaxDelay));

    public long Update(IWorldServices world)
    {
        if (!Entity.HasStatus(AflameBit)) return SimulationTiming.SleepForever;
        if (data.OclEmbers.Length > 0 && !data.OclEmbers.Equals("None", StringComparison.OrdinalIgnoreCase)) world.CreateFromList(Entity, data.OclEmbers, EntityId.Invalid, false);
        if (data.Range != Fixed.Zero) Spread(world);
        return NextDelay(world);
    }

    private void Spread(IWorldServices world)
    {
        var limit = SquaredReach.Of(data.Range);
        Entity? best = null;
        var bestDistance = Fixed.MaxValue;
        foreach (var other in world.Entities)
        {
            if (other.Id == Entity.Id || other.DestroyRequested) continue;
            var flammable = other.ModuleArray.OfType<FlammableUpdate>().FirstOrDefault();
            if (flammable is null || !flammable.WouldIgnite) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            var dz = other.Z - Entity.Z;
            var distance = dx * dx + dy * dy + dz * dz;
            if (!limit.Contains(distance) || distance > bestDistance || (distance == bestDistance && best is not null && other.Id.Value > best.Id.Value)) continue;
            best = other;
            bestDistance = distance;
        }
        best?.ModuleArray.OfType<FlammableUpdate>().First().TryToIgnite(world);
    }

    public override void Describe(IStateWriter writer) => writer.Int("nextWake", _nextWake);
    public override void Load(IStateReader reader) => _nextWake = reader.Int("nextWake");
}

// ================================================================================================================ DeployStyleAIUpdate

/// <summary><c>DeployStyleAIUpdate.h</c>: the AI base plus the time to unpack and to pack (ticks). The turret options and the manual animations are typed; the turrets themselves are the simulation's (docs/SIM-TURRET-AIM.md), the deploy-specific turret options are not modeled.</summary>
public sealed record DeployStyleData(AIUpdateData Base, long UnpackTicks, long PackTicks) : ModuleData
{
    public static DeployStyleData Parse(RatedModuleConfiguration configuration) => new(
        AIUpdateData.Parse(configuration), ModuleConfigReader.Ticks(configuration, "UnpackTime", 0), ModuleConfigReader.Ticks(configuration, "PackTime", 0));
}

/// <summary>
/// <c>DeployStyleAIUpdate.cpp</c>: a unit (Patriot battery, Sentry Drone, Tomahawk, Nuke Cannon, Inferno Cannon, ...) that must be set up to shoot and packed up to move. The five
/// states of the reference are kept: <c>READY_TO_MOVE</c> (mobile, cannot attack), <c>DEPLOY</c> (unpacking for <c>UnpackTime</c>), <c>READY_TO_ATTACK</c> (the <c>DEPLOYED</c> status is set),
/// <c>UNDEPLOY</c> (packing for <c>PackTime</c>) and <c>ALIGNING_TURRETS</c>. The game logic here has no AI state machine, so the two triggers of <c>update</c> are read where they surface:
/// a shot in range (<see cref="IFireGate"/>, asked when an accepted fire command executes or the unit fires at a victim it acquired itself, after range and readiness) starts deploying and is dropped until the
/// unit is deployed — the next attempt (the unit's own attack fires every tick it may, a command giver re-issues its order) succeeds afterwards — and a move order (<see cref="OrderMove"/>) starts undeploying and is carried out
/// when the unit is packed. An attack wanted while packing reverses the pack where it stands (<c>setMyState(DEPLOY, reverseDeploy)</c>, both directions take <c>UnpackTime</c> as in the
/// reference) and drops the pending move; an unpacking unit asked to move reverses likewise. While unpacking or packing the unit stands still. The idle auto deploy follows from the unit's own target acquisition (it deploys for a victim it acquired). Not modeled: turret recentering (<c>ALIGNING_TURRETS</c> is never entered: a packing unit does not wait for its turret to centre), <c>TurretsFunctionOnlyWhenDeployed</c>, the model conditions,
/// animations and sounds.
/// </summary>
public sealed class DeployStyleAIUpdate(DeployStyleData data) : AIUpdateInterface(data.Base), IFireGate
{
    private static readonly int DeployedBit = ObjectStatusBits.Index("DEPLOYED");

    private enum DeployState : byte { ReadyToMove, Deploy, ReadyToAttack, Undeploy, AligningTurrets }

    private DeployState _state;
    private long _frameToWait;
    private bool _hasPendingMove;
    private bool _pendingFromPlayer;
    private Fixed _pendingX;
    private Fixed _pendingY;

    public override string ModuleName => "DeployStyleAIUpdate";

    /// <summary>The unit is set up and may shoot.</summary>
    public bool IsDeployed => _state == DeployState.ReadyToAttack;

    /// <summary>The unit is packed and may move.</summary>
    public bool IsPacked => _state == DeployState.ReadyToMove;

    /// <summary><c>DEPLOY</c>: the reference shows <c>MODELCONDITION_UNPACKING</c> for exactly this state (<c>setMyState</c>).</summary>
    public bool IsUnpacking => _state == DeployState.Deploy;

    /// <summary><c>UNDEPLOY</c>: the reference shows <c>MODELCONDITION_PACKING</c> for exactly this state (<c>setMyState</c>).</summary>
    public bool IsPacking => _state == DeployState.Undeploy;

    public override void OrderMove(IWorldServices world, Fixed x, Fixed y, bool fromPlayer, bool attackMove = false)
    {
        if (_state == DeployState.ReadyToMove)
        {
            _hasPendingMove = false;
            base.OrderMove(world, x, y, fromPlayer, attackMove);
            return;
        }
        (_pendingX, _pendingY, _pendingFromPlayer, _hasPendingMove) = (x, y, fromPlayer, true);
        switch (_state)
        {
            case DeployState.ReadyToAttack: SetState(world, DeployState.Undeploy, false); break;
            case DeployState.Deploy when _frameToWait != 0: SetState(world, DeployState.Undeploy, true); break;
            case DeployState.AligningTurrets: SetState(world, DeployState.Undeploy, false); break;
        }
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        world.MarkDirty(Entity);
    }

    public bool AllowsFire(IWorldServices world, Entity victim)
    {
        switch (_state)
        {
            case DeployState.ReadyToAttack:
                return true;
            case DeployState.ReadyToMove:
                SetState(world, DeployState.Deploy, false);
                StopInternalMove();
                _hasPendingMove = false;
                break;
            case DeployState.Undeploy when _frameToWait != 0:
                SetState(world, DeployState.Deploy, true);
                _hasPendingMove = false;
                break;
            case DeployState.AligningTurrets:
                SetState(world, DeployState.ReadyToAttack, false);
                return true;
        }
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        world.MarkDirty(Entity);
        return false;
    }

    public override long Update(IWorldServices world)
    {
        var now = world.Tick;
        if (_frameToWait != 0 && now >= _frameToWait)
        {
            if (_state == DeployState.Deploy) SetState(world, DeployState.ReadyToAttack, false);
            else if (_state == DeployState.Undeploy) SetState(world, DeployState.ReadyToMove, false);
        }
        if (_state == DeployState.ReadyToMove && _hasPendingMove)
        {
            _hasPendingMove = false;
            base.OrderMove(world, _pendingX, _pendingY, _pendingFromPlayer);
        }
        // DEPLOY / UNDEPLOY: setLocomotorGoalNone() every tick — the unit does not move while it sets up or packs.
        if (_state is DeployState.Deploy or DeployState.Undeploy) StopInternalMove();
        var sleep = base.Update(world);
        // The reference never sleeps this AI so that any new command is caught; here a running timer or a waiting move keeps it awake.
        return _state is DeployState.Deploy or DeployState.Undeploy || _hasPendingMove ? SimulationTiming.SleepNone : sleep;
    }

    /// <summary><c>setMyState</c>: the status and timer effects (sounds and model conditions are presentation).</summary>
    private void SetState(IWorldServices world, DeployState state, bool reverse)
    {
        var now = world.Tick;
        _state = state;
        switch (state)
        {
            case DeployState.Deploy:
            case DeployState.Undeploy:
                if (state == DeployState.Undeploy) Entity.Status &= ~(1UL << DeployedBit);
                if (reverse)
                {
                    // The animation runs back from where it stands: the time already spent becomes the time to spend (both directions use UnpackTime in the reference).
                    var framesLeft = _frameToWait - now;
                    _frameToWait = now + data.UnpackTicks - framesLeft;
                }
                else _frameToWait = now + (state == DeployState.Deploy ? data.UnpackTicks : data.PackTicks);
                break;
            case DeployState.ReadyToMove:
                _frameToWait = 0;
                break;
            case DeployState.ReadyToAttack:
                Entity.Status |= 1UL << DeployedBit;
                _frameToWait = 0;
                break;
            case DeployState.AligningTurrets:
                _frameToWait = 0;
                break;
        }
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        if (_state != DeployState.ReadyToMove)
        {
            writer.Int("deployState", (int)_state);
            writer.Int("deployFrame", _frameToWait);
        }
        if (!_hasPendingMove) return;
        writer.Bool("pendingMove", true);
        writer.Bool("pendingFromPlayer", _pendingFromPlayer);
        writer.Fixed("pendingX", _pendingX);
        writer.Fixed("pendingY", _pendingY);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        var state = reader.Has("deployState") ? reader.Int("deployState") : 0;
        if (state < 0 || state > (int)DeployState.AligningTurrets) throw new InvalidDataException("Deploy state out of range.");
        _state = (DeployState)state;
        _frameToWait = reader.Has("deployFrame") ? reader.Int("deployFrame") : 0;
        _hasPendingMove = reader.Has("pendingMove") && reader.Bool("pendingMove");
        _pendingFromPlayer = _hasPendingMove && reader.Bool("pendingFromPlayer");
        _pendingX = _hasPendingMove ? reader.Fixed("pendingX") : Fixed.Zero;
        _pendingY = _hasPendingMove ? reader.Fixed("pendingY") : Fixed.Zero;
    }
}

// ================================================================================================================ FloatUpdate

/// <summary><c>FloatUpdate.cpp</c>: <c>Enabled</c> (default off).</summary>
public sealed record FloatData(bool Enabled) : ModuleData
{
    public static FloatData Parse(RatedModuleConfiguration configuration) => new(ModuleConfigReader.Bool(configuration, "Enabled", false));
}

/// <summary>
/// <c>FloatUpdate::update</c>: an enabled object is put on the water surface under it every tick (<c>isUnderwater</c> gives the level of the water area; outside every water area the
/// position is left alone, as the level is undefined there in the reference). Every shipped use (the buoys) has <c>Enabled = No</c> — "just swayed around like a buoy" — and the sway
/// (<c>setInstanceMatrix</c> with a sine of the frame number) only moves the model, so a disabled float object does nothing and sleeps.
/// </summary>
public sealed class FloatUpdate(FloatData data) : SimModule, IUpdateModule
{
    private long _nextWake = SimulationTiming.SleepForever;
    private bool _enabled;

    public override string ModuleName => "FloatUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        _enabled = data.Enabled;
        _nextWake = _enabled ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    public long Update(IWorldServices world)
    {
        if (!_enabled) return SimulationTiming.SleepForever;
        if (world.Terrain is { } terrain && terrain.TryWaterLevel(Entity.X, Entity.Y, out var level)) Entity.Z = level;
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("enabled", _enabled);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _enabled = reader.Bool("enabled");
    }
}

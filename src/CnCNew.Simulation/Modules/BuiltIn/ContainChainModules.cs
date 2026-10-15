using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The three <c>TransportContain</c> descendants of the retail data — <c>RiderChangeContain</c> (the combat bike and its swappable rider), <c>OverlordContain</c> (the
/// Overlord/Avenger and the structure on its back) and <c>InternetHackContain</c> (the Internet Center's hackers) — and <c>PassengersFireUpgrade</c>.
/// </summary>
internal static class ContainChainModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<InternetHackContainData>("InternetHackContain", ModuleOwnerKind.Object, (configuration, _) => InternetHackContainData.Parse(configuration), data => new InternetHackContain(data)),
        new ModuleBinding<OverlordContainData>("OverlordContain", ModuleOwnerKind.Object, (configuration, _) => OverlordContainData.Parse(configuration), data => new OverlordContain(data)),
        new ModuleBinding<UpgradeMuxOnlyData>("PassengersFireUpgrade", ModuleOwnerKind.Object, UpgradeMuxOnlyData.Parse, data => new PassengersFireUpgrade(data)),
        new ModuleBinding<RiderChangeContainData>("RiderChangeContain", ModuleOwnerKind.Object, (configuration, _) => RiderChangeContainData.Parse(configuration), data => new RiderChangeContain(data)),
    ];

    /// <summary>The creation of a contain module's payload (<c>createPayload</c>): the objects appear at the container and are put inside once they exist (the next tick).</summary>
    internal static void RequestPayload(IWorldServices world, Entity container, string name, List<EntityId> pending)
    {
        if (!world.Templates.TryGet(name, out var template))
            world.Emit(SimEventKind.CreationSkipped, container.Id, EntityId.Invalid, name, CreationSkipReasons.UnknownTemplate);
        else if (!template.IsInstantiable)
            world.Emit(SimEventKind.CreationSkipped, container.Id, EntityId.Invalid, name, CreationSkipReasons.TemplateNotInstantiable);
        else pending.Add(world.RequestSpawn(template.Index, container.Owner, container.X, container.Y, container.Z, container.Angle));
    }

    /// <summary>
    /// Puts every payload object that exists by now into <paramref name="container"/>; objects that are not valid riders stay outside, as the reference's assertion leaves them. Returns whether payload is still to appear.
    /// The container looks once per logic frame of the original, in the ticks its own frames begin with: the payload appears one frame after the container and is inside one
    /// frame after that, as at 30 Hz (where every tick is such a tick).
    /// </summary>
    internal static bool EnterPayload(IWorldServices world, Entity container, List<EntityId> pending)
    {
        if (!world.Time.BeginsFrameOf(container, world.Tick)) return pending.Count > 0;
        for (var index = 0; index < pending.Count;)
        {
            if (world.FindEntity(pending[index]) is not { } payload)
            {
                index++;
                continue;
            }
            world.TryEnterContainer(payload, container);
            pending.RemoveAt(index);
        }
        return pending.Count > 0;
    }

    internal static void DescribePending(IStateWriter writer, List<EntityId> pending)
    {
        if (pending.Count == 0) return;
        writer.BeginArray("pendingPayload");
        foreach (var id in pending)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    internal static void LoadPending(IStateReader reader, List<EntityId> pending)
    {
        pending.Clear();
        if (!reader.Has("pendingPayload")) return;
        foreach (var entry in reader.Array("pendingPayload")) pending.Add(new EntityId(checked((uint)entry.UInt("id"))));
    }
}

// ================================================================================================================ InternetHackContain

/// <summary><c>InternetHackContainModuleData</c> = <c>TransportContainModuleData</c> (<c>InternetHackContain.cpp</c> adds no field) plus the initial <c>PassengersAllowedToFire</c> flag.</summary>
public sealed record InternetHackContainData(TransportContainData Transport, bool PassengersAllowedToFire) : ModuleData
{
    public static InternetHackContainData Parse(RatedModuleConfiguration configuration)
        => new(TransportContainData.Parse(configuration), ModuleConfigReader.Bool(configuration, "PassengersAllowedToFire", false));
}

/// <summary>
/// <c>InternetHackContain.cpp</c>: a <c>TransportContain</c> that gives every rider the <c>aiHackInternet</c> command when it enters (<see cref="HackInternetAIUpdate.StartHacking"/>), so a
/// hacker inside the Internet Center starts hacking at once (with the faster <c>CashUpdateDelayFast</c>). A rider without <c>HackInternetAIUpdate</c> is just carried.
/// </summary>
public sealed class InternetHackContain(InternetHackContainData data) : TransportContain(data.Transport)
{
    public override string ModuleName => "InternetHackContain";

    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        PassengersAllowedToFire = data.PassengersAllowedToFire;
    }

    public override void Add(IWorldServices world, Entity candidate)
    {
        base.Add(world, candidate);
        if (candidate.ModuleArray.OfType<HackInternetAIUpdate>().FirstOrDefault() is { } hacker) hacker.StartHacking(world);
    }
}

// =============================================================================================================== PassengersFireUpgrade

/// <summary>
/// <c>PassengersFireUpgrade.cpp</c>: the upgrade lets the passengers of the object's container fire (<c>setPassengerAllowedToFire(TRUE)</c>). The flag is recorded on the
/// container (<see cref="ContainModuleBase.PassengersAllowedToFire"/>); firing from inside a container does not exist in this simulation, so no code reads it yet. The
/// reference never switches it off again.
/// </summary>
public sealed class PassengersFireUpgrade(UpgradeMuxOnlyData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "PassengersFireUpgrade";

    protected override void Activate(IWorldServices world)
    {
        if (Entity.Container is ContainModuleBase contain) contain.PassengersAllowedToFire = true;
    }
}

// ================================================================================================================== RiderChangeContain

/// <summary>One <c>RiderN</c> line of <c>RiderChangeContain</c>: the infantry type that may ride, and what riding it sets on the vehicle.</summary>
public sealed record RiderInfo(string TemplateName, string ModelCondition, string WeaponSet, int StatusBit, string CommandSet, string LocomotorSet);

/// <summary>
/// <c>RiderChangeContainModuleData</c> (<c>RiderChangeContain.cpp</c>): the transport fields, the up to eight rider lines (<c>parseRiderInfo</c>: template, model condition,
/// weapon set flag, object status, command set, locomotor set), <c>ScuttleDelay</c> (frames until a deserted vehicle blows itself up) and the single
/// <c>InitialPayload</c> (the rider the vehicle starts with). The status must be an object status of the engine, the weapon set flag a weapon-set condition and the locomotor
/// set one of the locomotor set names; a line that names none is a data error, never a silent default.
/// </summary>
public sealed record RiderChangeContainData(TransportContainData Transport, bool PassengersAllowedToFire, ImmutableArray<RiderInfo> Riders, long ScuttleTicks, string InitialPayload, int InitialPayloadCount) : ModuleData
{
    public static RiderChangeContainData Parse(RatedModuleConfiguration configuration)
    {
        var riders = ImmutableArray.CreateBuilder<RiderInfo>();
        for (var slot = 1; slot <= 8; slot++)
        {
            var tokens = ModuleConfigReader.Names(configuration, $"Rider{slot}");
            if (tokens.IsEmpty) continue;
            if (!NameTables.ObjectStatus.TryIndexOf(tokens[3], out var status)) throw new InvalidDataException($"RiderChangeContain: Rider{slot} names the unknown object status '{tokens[3]}'.");
            if (!NameTablesPhase5.WeaponSetCondition.TryIndexOf(tokens[2], out _)) throw new InvalidDataException($"RiderChangeContain: Rider{slot} names the unknown weapon set flag '{tokens[2]}'.");
            if (!NameTables.LocomotorSet.TryIndexOf(tokens[5], out _)) throw new InvalidDataException($"RiderChangeContain: Rider{slot} names the unknown locomotor set '{tokens[5]}'.");
            riders.Add(new RiderInfo(tokens[0], tokens[1], tokens[2], status, tokens[4], tokens[5]));
        }
        var payload = ModuleConfigReader.Find(configuration, "InitialPayload")?.Value.Value as CountedReference;
        return new RiderChangeContainData(TransportContainData.Parse(configuration), ModuleConfigReader.Bool(configuration, "PassengersAllowedToFire", false),
            riders.ToImmutable(), ModuleConfigReader.Ticks(configuration, "ScuttleDelay", 0), payload?.Reference.Name ?? string.Empty, payload?.Count ?? 0);
    }
}

/// <summary>
/// <c>RiderChangeContain.cpp</c>: a vehicle (the combat bike) that one infantry rider drives; the rider's type decides what the vehicle becomes. Only the types named by
/// <c>Rider1</c>..<c>Rider8</c> may board, and boarding does not check capacity because the new rider throws the old one out (<c>aiEvacuateInstantly</c>, which also clears the
/// old rider's status); the rider's <c>STATUS_RIDERn</c> object status is set on the vehicle, and the bike takes the rider's veterancy (raised only — the rider keeps its own).
/// The initial payload boards the vehicle once it exists (the next tick), without throwing anybody out. When the rider leaves and is not replaced, the vehicle is scuttled:
/// unselectable (and immobile when it stands still), and <c>ScuttleDelay</c> frames later it dies of <c>TOPPLED</c>; a scuttled vehicle accepts nobody. When the vehicle dies, its
/// rider is destroyed with it. The weapon set, command set and locomotor set of the rider line, and the model condition, are not applied: a template has one weapon set per
/// condition, no command-set override and a single locomotor, and the model is presentation. Selection transfer is presentation.
/// </summary>
public sealed class RiderChangeContain(RiderChangeContainData data) : TransportContain(data.Transport)
{
    private static readonly int UnselectableBit = ObjectStatusBits.Index("UNSELECTABLE");
    private static readonly int ImmobileBit = ObjectStatusBits.Index("IMMOBILE");
    private static readonly int ToppledDeath = NameTables.DeathType.TryIndexOf("TOPPLED", out var toppled) ? toppled : throw new InvalidOperationException("TOPPLED is not a death type.");

    private readonly List<EntityId> _pendingPayload = [];
    private bool _payloadCreated;
    private bool _replacing;
    private int _riderIndex = -1;
    private long _scuttledTick = -1;

    public override string ModuleName => "RiderChangeContain";

    /// <summary>The rider left without a replacement: the vehicle is on its way to a topple death.</summary>
    public bool IsScuttled => _scuttledTick >= 0;

    /// <summary>Which of the rider lines the current rider matches (-1: nobody, or a rider no line names).</summary>
    public int RiderIndex => _riderIndex;

    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        PassengersAllowedToFire = data.PassengersAllowedToFire;
        if (data.InitialPayload.Length == 0)
        {
            _payloadCreated = true;
            return;
        }
        for (var index = 0; index < Math.Max(data.InitialPayloadCount, 1); index++) ContainChainModules.RequestPayload(world, Entity, data.InitialPayload, _pendingPayload);
        if (_pendingPayload.Count == 0) _payloadCreated = true;
        else NextWake = world.Tick + SimulationTiming.SleepNone;
    }

    private int LineOf(Entity candidate)
    {
        for (var index = 0; index < data.Riders.Length; index++)
            if (data.Riders[index].TemplateName.Equals(candidate.Template.Name, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }

    public override bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
        => base.IsValidContainerFor(world, candidate, false) && _scuttledTick < 0 && LineOf(candidate) >= 0;

    public override void Add(IWorldServices world, Entity candidate)
    {
        var isPayload = _pendingPayload.Contains(candidate.Id);
        if (!isPayload && _payloadCreated && Riders.Count > 0)
        {
            // onContaining: "Remove our existing rider".
            _replacing = true;
            foreach (var id in Riders.ToArray())
                if (world.FindEntity(id) is { } old)
                {
                    Remove(world, old, true);
                    world.Emit(SimEventKind.EntityUncontained, old.Id, Entity.Id, string.Empty, 0);
                }
            _replacing = false;
        }
        base.Add(world, candidate);
        var line = LineOf(candidate);
        if (line >= 0)
        {
            _riderIndex = line;
            Entity.Status |= 1UL << data.Riders[line].StatusBit;
        }
        // Transfer experience from the rider to the bike.
        world.SetMinVeterancyLevel(Entity, candidate.VeterancyLevel);
    }

    public override void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        var wasRider = Riders.Contains(candidate.Id);
        base.Remove(world, candidate, positionAtContainer);
        if (!wasRider) return;
        if (_riderIndex >= 0)
        {
            Entity.Status &= ~(1UL << data.Riders[_riderIndex].StatusBit);
            _riderIndex = -1;
        }
        // "Note if the bike dies, the rider dies too."
        if (Entity.EffectivelyDead || Entity.DestroyRequested)
        {
            world.RequestDestroy(candidate.Id);
            return;
        }
        if (_replacing) return;
        // Finally, scuttle the bike so nobody else can use it.
        _scuttledTick = world.Tick;
        Entity.Status |= 1UL << UnselectableBit;
        if (!Entity.HasMoveTarget) Entity.Status |= 1UL << ImmobileBit;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public override long Update(IWorldServices world)
    {
        // First the door (a rider that leaves in this update scuttles the bike, and the scuttle has to keep the module awake).
        var sleep = base.Update(world);
        var waiting = false;
        if (_pendingPayload.Count > 0)
        {
            waiting = ContainChainModules.EnterPayload(world, Entity, _pendingPayload);
            if (!waiting) _payloadCreated = true;
        }
        if (_scuttledTick >= 0 && !Entity.EffectivelyDead)
        {
            if (world.Tick - _scuttledTick >= data.ScuttleTicks && Entity.Body is { } body)
                world.Damage(Entity.Id, new DamageInfo(EntityId.Invalid, body.MaxHealth, DamageTypes.Unresistable, ToppledDeath, true));
            else waiting = true;
        }
        return waiting ? SimulationTiming.SleepNone : sleep;
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.Bool("payloadCreated", _payloadCreated);
        writer.Int("riderIndex", _riderIndex);
        writer.Int("scuttledTick", _scuttledTick);
        ContainChainModules.DescribePending(writer, _pendingPayload);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _payloadCreated = reader.Bool("payloadCreated");
        _riderIndex = checked((int)reader.Int("riderIndex"));
        if (_riderIndex < -1 || _riderIndex >= data.Riders.Length) throw new InvalidDataException("Rider line out of range.");
        _scuttledTick = reader.Int("scuttledTick");
        ContainChainModules.LoadPending(reader, _pendingPayload);
    }
}

// ================================================================================================================== OverlordContain

/// <summary>
/// <c>OverlordContainModuleData</c> (<c>OverlordContain.cpp</c>): the transport fields plus <c>PayloadTemplateName</c> (the objects created inside at construction) and
/// <c>ExperienceSinkForRider</c> (the rider's experience goes to the Overlord; no experience sinks exist here, so it is typed and unused).
/// </summary>
public sealed record OverlordContainData(TransportContainData Transport, bool PassengersAllowedToFire, ImmutableArray<string> Payload, bool ExperienceSinkForRider) : ModuleData
{
    public static OverlordContainData Parse(RatedModuleConfiguration configuration) => new(
        TransportContainData.Parse(configuration), ModuleConfigReader.Bool(configuration, "PassengersAllowedToFire", false),
        ModuleConfigReader.AccumulatedNames(configuration, "PayloadTemplateName"), ModuleConfigReader.Bool(configuration, "ExperienceSinkForRider", true));
}

/// <summary>
/// <c>OverlordContain.cpp</c>: "like Transport, but when full, passes transport queries along to first passenger". The Overlord carries a <c>PORTABLE_STRUCTURE</c> (gattling
/// cannon, propaganda tower, battle bunker; the Avenger's laser turret is created with it, <c>PayloadTemplateName</c>); once one boards, the Overlord redirects capacity,
/// validity, entry, exit, count and the list of passengers to that structure's own container (<c>getRedirectedContain</c>), so infantry enter and leave the bunker on its back.
/// The structure follows the Overlord's position and heading every tick, dies with it (the reference orders the death: the rider first, redirection off) and is destroyed when
/// the Overlord is removed. A second structure is refused by the transport's own slot count. The experience sink, capture handling, stealth sharing and the damage-state
/// mirroring are not simulated. Entering a redirected container leaves <see cref="Entity.ContainedBy"/> pointing at the Overlord (the world sets it after <c>Add</c>), while
/// the passenger is listed by the structure.
/// </summary>
public sealed class OverlordContain(OverlordContainData data) : TransportContain(data.Transport), IContainerModule, IWorldAttached
{
    private readonly List<EntityId> _pendingPayload = [];
    private IWorldServices? _world;
    private bool _redirectionActivated;

    public override string ModuleName => "OverlordContain";
    public bool Redirecting => Redirected is not null;

    public void Attach(IWorldServices world) => _world = world;

    /// <summary><c>getRedirectedContain</c>: the container of the first passenger, once redirection is on.</summary>
    private IContainerModule? Redirected
        => _redirectionActivated && Riders.Count > 0 && _world?.FindEntity(Riders[0]) is { DestroyRequested: false } rider ? rider.Container : null;

    public override int ContainMax => Redirected?.ContainMax ?? base.ContainMax;
    /// <summary>The Overlord's own first rider (its portable structure): <c>OverlordContain::isEnclosingContainerFor</c> does not enclose it.</summary>
    public EntityId FirstRider => Riders.Count > 0 ? Riders[0] : EntityId.Invalid;

    int IContainerModule.ContainCount => Redirected?.ContainCount ?? Riders.Count;
    IReadOnlyList<EntityId> IContainerModule.Contained => Redirected?.Contained ?? Riders;

    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        PassengersAllowedToFire = data.PassengersAllowedToFire;
        foreach (var name in data.Payload) ContainChainModules.RequestPayload(world, Entity, name, _pendingPayload);
        if (_pendingPayload.Count > 0) NextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public override bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
        => Redirected is { } redirected ? redirected.IsValidContainerFor(world, candidate, checkCapacity) : base.IsValidContainerFor(world, candidate, checkCapacity);

    public override void Add(IWorldServices world, Entity candidate)
    {
        if (Redirected is { } redirected)
        {
            redirected.Add(world, candidate);
            return;
        }
        base.Add(world, candidate);
        if (candidate.Template.IsKindOf("PORTABLE_STRUCTURE"))
        {
            _redirectionActivated = true;
            Follow(candidate);
            world.WakeAt(Entity, this, world.Tick);
        }
    }

    public override void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        if (Redirected is { } redirected)
        {
            redirected.Remove(world, candidate, positionAtContainer);
            return;
        }
        base.Remove(world, candidate, positionAtContainer);
        if (Riders.Count == 0) _redirectionActivated = false;
    }

    private void Follow(Entity rider)
    {
        rider.X = Entity.X;
        rider.Y = Entity.Y;
        rider.Z = Entity.Z;
        rider.Angle = Entity.Angle;
    }

    public override long Update(IWorldServices world)
    {
        var waiting = _pendingPayload.Count > 0 && ContainChainModules.EnterPayload(world, Entity, _pendingPayload);
        if (_redirectionActivated && Riders.Count > 0 && world.FindEntity(Riders[0]) is { DestroyRequested: false } structure)
        {
            Follow(structure);
            world.MarkDirty(structure);
            waiting = true;
        }
        var sleep = base.Update(world);
        return waiting ? SimulationTiming.SleepNone : sleep;
    }

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        // "Do you mean me the Overlord, or my behavior of passing stuff on to my passengers?": with a redirect the structure dies first, with the redirect off.
        if (Redirected is not null && Riders.Count > 0)
        {
            _redirectionActivated = false;
            world.Kill(Riders[0]);
        }
        base.OnDie(world, info);
    }

    public override void OnRemove(IWorldServices world)
    {
        foreach (var id in Riders.ToArray()) world.RequestDestroy(id);
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.Bool("redirection", _redirectionActivated);
        ContainChainModules.DescribePending(writer, _pendingPayload);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _redirectionActivated = reader.Bool("redirection");
        ContainChainModules.LoadPending(reader, _pendingPayload);
    }
}

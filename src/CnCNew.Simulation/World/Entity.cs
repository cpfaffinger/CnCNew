using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>Scheduler node of one update module; the heap orders nodes by (wake tick, entity id, module index).</summary>
internal sealed class UpdateNode(Entity entity, SimModule module, IUpdateModule update)
{
    public Entity Entity { get; } = entity;
    public SimModule Module { get; } = module;
    public IUpdateModule Update { get; } = update;
    public int HeapIndex { get; set; } = -1;
}

/// <summary>
/// A live object. All mutable state is fixed-point or integer. Modules mutate the entity only while the world dispatches a
/// call to them; the world marks the entity dirty at every dispatch, which drives the incremental state hash.
/// </summary>
public sealed class Entity
{
    internal Entity(EntityId id, ObjectTemplate template, PlayerId owner, long spawnTick)
    {
        Id = id;
        Template = template;
        Owner = owner;
        SpawnTick = spawnTick;
    }

    public EntityId Id { get; }
    public ObjectTemplate Template { get; }
    public PlayerId Owner { get; internal set; }
    public Fixed X { get; internal set; }
    public Fixed Y { get; internal set; }
    public Fixed Z { get; internal set; }

    /// <summary>
    /// What is left of the step of the logic frame of the original this object is covering in shares, one per tick (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3): the module that
    /// moves the object publishes it when it decides a frame and takes each share off it. Zero between two frames – and always at 30 Hz, where a frame is one tick: nothing
    /// ever writes it there, and it is described only while it is not zero.
    /// </summary>
    public Fixed AheadX { get; private set; }
    public Fixed AheadY { get; private set; }
    public Fixed AheadZ { get; private set; }

    /// <summary>The tick in which the mover last published or covered a share of <see cref="AheadX"/>; a rest nobody has touched since the tick before the reader's is stale.</summary>
    public long AheadTick { get; private set; }

    /// <summary>True while a decided frame's step is not covered yet. Never at 30 Hz.</summary>
    public bool IsInsideFrameStep => (AheadX.Raw | AheadY.Raw | AheadZ.Raw) != 0;

    /// <summary>
    /// Where the object stands at the end of the frame it has begun: what an observer that takes its own decision of the frame in tick <paramref name="tick"/> has to read, because
    /// that is what it sees at 30 Hz (a mover that already had its turn in the tick has taken its whole step, one that has not is still where its last step left it). A rest
    /// whose mover has not run in this tick or the one before (it was stopped, disabled or put to sleep inside its frame) no longer counts. 30 Hz: the position.
    /// </summary>
    public (Fixed X, Fixed Y, Fixed Z) FrameEnd(long tick)
        => IsInsideFrameStep && AheadTick + TickOrder.Next >= tick ? (X + AheadX, Y + AheadY, Z + AheadZ) : (X, Y, Z);

    /// <summary>The mover's side of <see cref="AheadX"/>: the whole step of the frame it has just decided (the shares follow with <see cref="CoverShare"/>), or zero to drop a rest.</summary>
    internal void SetAhead(long tick, Fixed x, Fixed y, Fixed z)
    {
        AheadX = x;
        AheadY = y;
        AheadZ = z;
        AheadTick = tick;
        HasAheadAngle = false;
    }

    /// <summary>The mover's side of <see cref="AheadZ"/> for a mover that follows the ground: the height its begun frame ends with, less its height now.</summary>
    internal void SetAheadHeight(Fixed z) => AheadZ = z;

    /// <summary>The mover's side of <see cref="AheadX"/>: covers one more share of the frame's step. The shares of a frame add up to its step, so the rest ends at zero.</summary>
    internal void CoverShare(long tick, Fixed x, Fixed y, Fixed z)
    {
        X += x;
        Y += y;
        Z += z;
        AheadX -= x;
        AheadY -= y;
        AheadZ -= z;
        AheadTick = tick;
    }

    /// <summary>
    /// Puts the object where its begun frame ends and leaves nothing of the step to cover: for an event of the frame's decision tick that the 30 Hz step lets happen at the
    /// position after the whole step (a death by height). The mover sees the rest gone and adds no further share of that frame.
    /// </summary>
    internal void CompleteFrame(long tick)
    {
        Angle = FrameEndAngle(tick);
        HasAheadAngle = false;
        (X, Y, Z) = FrameEnd(tick);
        AheadX = AheadY = AheadZ = Fixed.Zero;
    }

    /// <summary>
    /// The heading the object has at the end of the frame it is covering, while it turns toward it in shares (the heading's counterpart of <see cref="AheadX"/>, with the same
    /// staleness rule through <see cref="AheadTick"/>). Never set at 30 Hz, described only while set.
    /// </summary>
    public Fixed AheadAngle { get; private set; }
    public bool HasAheadAngle { get; private set; }

    /// <summary>The heading at the end of the frame the object has begun: what an observer deciding in tick <paramref name="tick"/> reads (see <see cref="FrameEnd"/>). 30 Hz: the heading.</summary>
    public Fixed FrameEndAngle(long tick) => HasAheadAngle && AheadTick + TickOrder.Next >= tick ? AheadAngle : Angle;

    /// <summary>The mover's side of <see cref="AheadAngle"/>: the heading its decided frame ends with; the mover touches <see cref="AheadTick"/> with every share (<see cref="CoverShare"/>).</summary>
    internal void SetAheadAngle(Fixed angle)
    {
        AheadAngle = angle;
        HasAheadAngle = true;
    }

    /// <summary>The mover's side of <see cref="AheadAngle"/>: the frame's turn is covered (or dropped).</summary>
    internal void ClearAheadAngle() => HasAheadAngle = false;

    /// <summary>Heading in radians.</summary>
    public Fixed Angle { get; internal set; }

    /// <summary>Bits of the object status table (<see cref="ObjectStatusBits"/>).</summary>
    public ulong Status { get; internal set; }

    /// <summary>0 regular, 1 veteran, 2 elite, 3 heroic.</summary>
    public int VeterancyLevel { get; internal set; }

    /// <summary><c>ExperienceTracker::m_currentExperience</c>: raised by enemy kills of trainable objects, set to the level's requirement by explicit level changes.</summary>
    public int Experience { get; internal set; }

    /// <summary><c>ExperienceTracker::isTrainable</c>: the template's <c>IsTrainable</c>, unless something set it (<see cref="TrainableOverride"/>).</summary>
    public bool IsTrainable => TrainableOverride ?? Template.IsTrainable;

    /// <summary><c>ExperienceTracker::setTrainable</c> when it differs from the template's (the rule fix <c>bike-trainable-from-rider</c>); saved only when set.</summary>
    internal bool? TrainableOverride { get; set; }

    /// <summary><c>ExperienceTracker::m_experienceScalar</c>: multiplier of every experience gain (raised by <c>ExperienceScalarUpgrade</c>).</summary>
    public Fixed ExperienceScalar { get; internal set; } = Fixed.One;

    /// <summary>The set <c>WeaponBonusCondition</c> bits (<c>Object::m_weaponBonusCondition</c>): the veterancy level and <c>PLAYER_UPGRADE</c> are modeled.</summary>
    public uint WeaponBonusConditions { get; internal set; }

    /// <summary>Object-scoped completed-upgrade mask (<c>Object::getObjectCompletedUpgradeMask</c>); most objects never set a bit.</summary>
    public UpgradeMask CompletedUpgrades { get; internal set; }

    /// <summary>Current forward speed (dist/tick); moves along <see cref="Angle"/>. 0 unless the object has an active move order.</summary>
    public Fixed Speed { get; internal set; }

    /// <summary>
    /// Above 30 Hz only: the speed per logic frame of the original that <see cref="Speed"/> (per tick) is the share of, as the mover that set it computed it (the division by the ticks
    /// per frame loses its last bits). Two AI modules of one object hand their speed to each other through it. Never set at 30 Hz, described only when set.
    /// </summary>
    internal Fixed SpeedPerFrame { get; set; }

    public bool HasMoveTarget { get; internal set; }
    public Fixed MoveTargetX { get; internal set; }
    public Fixed MoveTargetY { get; internal set; }

    /// <summary>Rounds remaining in the current clip of the object's single (primary) weapon; -1 if the object has none.</summary>
    public int WeaponClip { get; internal set; } = -1;

    /// <summary>Tick at which the weapon may fire again (reload or shot delay, whichever is later).</summary>
    public long WeaponReadyTick { get; internal set; }

    /// <summary>Per-slot weapon state of an object with more than one weapon (<see cref="Templates.ObjectTemplate.HasMultipleWeaponSlots"/>); null for a single-weapon object, whose only slot lives in <see cref="WeaponClip"/>/<see cref="WeaponReadyTick"/>.</summary>
    internal WeaponSlotState? WeaponSlots { get; set; }

    /// <summary>Rounds left in <paramref name="slot"/>'s clip (-1: the slot holds no weapon, <see cref="int.MaxValue"/>: unlimited). Slot 0 is <see cref="WeaponClip"/>.</summary>
    public int ClipOf(int slot) => slot == 0 ? WeaponClip : WeaponSlots?.Clip[slot] ?? -1;

    /// <summary>Tick at which <paramref name="slot"/> may fire again. Slot 0 is <see cref="WeaponReadyTick"/>.</summary>
    public long ReadyTickOf(int slot) => slot == 0 ? WeaponReadyTick : WeaponSlots?.Ready[slot] ?? 0;

    /// <summary>Tick of the last shot fired from <paramref name="slot"/> (-1: never); for a single-weapon object that is <see cref="LastFireTick"/>.</summary>
    public long LastFireTickOf(int slot) => WeaponSlots is { } slots ? slots.LastFire[slot] : slot == 0 ? LastFireTick : -1;

    /// <summary><c>WeaponSet::m_curWeapon</c>: the slot the next unordered shot uses (the last chosen or locked one; primary for a single-weapon object).</summary>
    public int CurrentWeaponSlot => WeaponSlots?.Current ?? 0;

    /// <summary><c>WeaponSet::m_curWeaponLockedStatus</c>.</summary>
    public WeaponLockKind WeaponLock => WeaponSlots?.Lock ?? WeaponLockKind.None;

    /// <summary>The state of the object's turrets (<c>AIUpdateInterface::m_turretAI</c>), indexed like <see cref="Templates.TurretSet.Turrets"/>; null when the template declares none.</summary>
    internal TurretState?[]? Turrets { get; set; }

    /// <summary>The continuous-fire counters (<c>FiringTracker</c>); null until the object fires a weapon with continuous-fire stages and again once they are back at rest.</summary>
    internal FiringState? Firing { get; set; }

    /// <summary>
    /// The angle (relative to the body) and pitch of one turret and whether it is still turning (<c>AIUpdateInterface::getTurretRotAndPitch</c>, <c>MODELCONDITION_TURRET_ROTATE</c>);
    /// null when the object has no such turret. 0 is the main <c>Turret</c>, 1 the <c>AltTurret</c>.
    /// </summary>
    public TurretReading? TurretOf(int index)
        => Turrets is { } turrets && index >= 0 && index < turrets.Length && turrets[index] is { } state ? new TurretReading(state.Angle, state.Pitch, state.Rotating) : null;

    /// <summary>The unit's own attack state (its victim, an attack-move); null while it attacks nothing and marches nowhere, so such an object keeps its serialized form.</summary>
    internal AcquisitionState? Acquisition { get; set; }

    /// <summary>
    /// <c>ActiveBody::m_lastDamageTimestamp</c>: the tick of the last damage record (-1: none). <c>attemptDamage</c> records applied damage - within the frame of the record and the
    /// next one only a damage from a vehicle, infantry or faction structure replaces it -, <c>attemptHealing</c> records every heal (<see cref="Modules.BuiltIn.ActiveBody"/>).
    /// </summary>
    public long LastDamageTick { get; internal set; } = -1;

    /// <summary><c>m_lastDamageInfo.in.m_sourceID</c> of the last record; written only with a record.</summary>
    public EntityId LastDamageSource { get; internal set; } = EntityId.Invalid;

    /// <summary>The last record is a heal (<c>DAMAGE_HEALING</c>), which <c>StealthUpdate</c> does not count as damage; written only when set.</summary>
    public bool LastDamageHealing { get; internal set; }

    /// <summary><c>Object::isCaptured</c> (<c>setCaptured</c>, never cleared): the object was taken over by entering it (an unmanned vehicle); counted by the skirmish condition on captured units. Written only when set.</summary>
    public bool Captured { get; internal set; }

    /// <summary>Tick of the object's last <c>FireWeaponCommand</c>, for phase-5's coarse "is firing" approximation (-1: never).</summary>
    public long LastFireTick { get; internal set; } = -1;

    /// <summary>Set by <c>ArmorUpgrade</c> once completed: damage resolution reads <see cref="Templates.ObjectTemplate.PlayerUpgradeArmor"/> instead of the default set.</summary>
    public bool HasPlayerUpgradeArmor { get; internal set; }

    /// <summary>Set by <c>WeaponSetUpgrade</c> once completed: the <c>PLAYER_UPGRADE</c> weapon set flag (<see cref="SimulationWorld.WeaponSetFlagsOf"/>), which selects the object's <c>PLAYER_UPGRADE</c> <c>WeaponSet</c>.</summary>
    public bool HasPlayerUpgradeWeapon { get; internal set; }

    /// <summary><c>WEAPONSET_CRATEUPGRADE_ONE</c> (1) / <c>_TWO</c> (2) of <c>SalvageCrateCollide</c>: the salvager's weapon-set upgrade level; 0 (the flag cleared) is never written.</summary>
    public int CrateWeaponLevel { get; internal set; }

    /// <summary><c>ARMORSET_CRATE_UPGRADE_ONE</c> (1) / <c>_TWO</c> (2) of <c>SalvageCrateCollide</c>: the salvager's armor-set upgrade level; 0 is never written.</summary>
    public int CrateArmorLevel { get; internal set; }

    /// <summary><c>EFFECTIVELY_DEAD</c>: set by bodies when health reaches zero (always for inactive bodies).</summary>
    public bool EffectivelyDead { get; set; }

    /// <summary><c>DISABLED_EMP</c> until this tick (<c>Object::setDisabledUntil</c> by <c>EMPUpdate</c>/<c>LeafletDropBehavior</c>); 0 when never disabled. Movement and weapons stand still while it lies ahead.</summary>
    public long EmpDisabledUntil { get; internal set; }

    /// <summary><c>DISABLED_UNDERPOWERED</c>: a <c>POWERED</c> object of a player in a brown-out (<c>Player::onPowerBrownOutChange</c>).</summary>
    public bool Underpowered { get; internal set; }

    /// <summary><c>DISABLED_PARALYZED</c> (<c>BattlePlanUpdate</c>'s change of plan) up to this tick (the first tick it no longer holds); 0 when never paralyzed.</summary>
    public long ParalyzedUntil { get; internal set; }

    /// <summary>Script disables: bit 1 = <c>DISABLED_HELD</c> (<c>NAMED_SET_HELD</c>: nothing acts), bit 2 = <c>OBJECT_STATUS_SCRIPT_DISABLED</c> (<c>PLAYER_DISABLE_FACTORIES</c>: the object produces nothing). 0 when never touched.</summary>
    public byte ScriptDisableMask { get; internal set; }

    public const byte ScriptHeld = 1;
    public const byte ScriptDisabled = 2;

    /// <summary>The object's own disables at <paramref name="tick"/> (EMP, paralyzed, script hold, script disable); <see cref="Modules.IWorldServices.DisabledFlagsOf"/> adds its container's hold.</summary>
    public DisabledMask DisabledFlags(long tick)
    {
        var mask = EmpDisabledUntil > tick ? DisabledMask.Emp : DisabledMask.None;
        if (ParalyzedUntil > tick) mask |= DisabledMask.Paralyzed;
        if (Underpowered) mask |= DisabledMask.Underpowered;
        if ((ScriptDisableMask & ScriptHeld) != 0) mask |= DisabledMask.Held;
        if ((ScriptDisableMask & ScriptDisabled) != 0) mask |= DisabledMask.ScriptDisabled;
        return mask;
    }

    /// <summary><c>SlowDeathBehavior::onDie</c>'s "another AI already handled us" guard, generalized to the entity (not tied to <c>AIUpdateInterface</c>): the weighted sibling lottery runs at most once per death.</summary>
    public bool SlowDeathStarted { get; internal set; }

    public bool DestroyRequested { get; internal set; }

    /// <summary>
    /// Above 30 Hz only: the first tick whose destroy phase may remove the object. An object whose removal is requested after the destroy phase of a tick is removed "in the
    /// next tick" at 30 Hz, which is the next logic frame of the original (docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md). Never set at 30 Hz, described only when set.
    /// </summary>
    internal long DestroyNotBefore { get; set; }
    public long SpawnTick { get; }

    /// <summary>Build ticks already invested while the object is <c>UNDER_CONSTRUCTION</c> (<c>Object::m_constructionPercent</c>, in ticks of its build time).</summary>
    public int ConstructionWork { get; internal set; }

    /// <summary>
    /// <c>Object::m_constructionPercent</c> as a share of <see cref="ConstructionProgressScale"/>: every step of builder work adds its share of the build time the player's power
    /// gives at that step (<c>DozerAIUpdate</c>: <c>100 / calcTimeToBuild</c> per frame), rounded up.
    /// </summary>
    public long ConstructionProgress { get; internal set; }

    /// <summary>The whole of <see cref="ConstructionProgress"/> (2^40: the rounding of the longest build stays far below one step).</summary>
    public const long ConstructionProgressScale = 1L << 40;

    /// <summary>How far the construction is, 0 to 1, for readers outside the simulation.</summary>
    public Fixed ConstructionShare => Fixed.FromRaw(Math.Min(ConstructionProgress, ConstructionProgressScale) / (ConstructionProgressScale / Fixed.One.Raw));

    /// <summary>The dozer or worker that placed this construction site (<c>Object::m_builderID</c>); invalid for everything else.</summary>
    public EntityId BuilderId { get; internal set; } = EntityId.Invalid;

    /// <summary>Tick at which a sale completes (<c>BuildAssistant::update</c> sell countdown); -1 while the object is not being sold.</summary>
    public long SellCompleteTick { get; internal set; } = -1;

    /// <summary>The container currently holding this entity, if any (<c>Object::getContainedBy</c>). <see cref="Ids.EntityId.Invalid"/> when not contained.</summary>
    public EntityId ContainedBy { get; internal set; } = EntityId.Invalid;

    public IReadOnlyList<SimModule> Modules => ModuleArray;
    internal SimModule[] ModuleArray { get; set; } = [];
    public IBodyModule? Body { get; internal set; }
    /// <summary>The entity's own container module, if it has one (<c>ContainModuleInterface::asOpenContain</c>). One per entity, matching phase 5's scope.</summary>
    public IContainerModule? Container { get; internal set; }
    internal IDieModule[] Dies { get; set; } = [];
    internal IEventReceiver[] Receivers { get; set; } = [];
    internal IUpgradeModule[] UpgradeModules { get; set; } = [];
    internal IDamageModule[] DamageModules { get; set; } = [];
    /// <summary>The entity's collide modules (<see cref="ICollideModule"/>), filled when the world registers the entity.</summary>
    internal ICollideModule[] CollideModules { get; set; } = [];

    /// <summary>
    /// <c>Object::m_visionSpiedBy</c>: per spying player id, the reference count of "looks through this object's eyes" (<c>SpyVisionUpdate</c>); <see langword="null"/> for an object nobody ever spied on.
    /// A player is spying while its count is above zero (see <see cref="IsVisionSpiedBy"/>); the world reveals the object's shroud clearing range to every spying player.
    /// </summary>
    internal SortedList<int, int>? VisionSpiedBy { get; set; }

    /// <summary>Factor on the template's shroud clearing range (<c>Object::setShroudClearingRange</c>: the strategy center's search-and-destroy bonus); 1 for every other object.</summary>
    public Fixed ShroudScalar { get; internal set; } = Fixed.One;

    public bool IsVisionSpiedBy(PlayerId player) => VisionSpiedBy is { } spied && spied.TryGetValue(player.Value, out var count) && count > 0;
    internal UpdateNode[] Updates { get; set; } = [];
    internal List<Timers.SimTimer>? Timers { get; set; }
    internal bool Registered { get; set; }
    internal bool Dirty { get; set; }
    internal ulong CachedHash { get; set; }

    public bool HasStatus(int bit) => (Status & (1UL << bit)) != 0;

    /// <summary>Written only while some turret is away from rest, so an object whose turrets never moved keeps its serialized form (and hash).</summary>
    private void DescribeTurrets(IStateWriter writer)
    {
        if (Turrets is not { } turrets || Template.Turrets is not { } set) return;
        var any = false;
        for (var index = 0; index < turrets.Length; index++)
            if (turrets[index] is { } state && set[index] is { } info && !state.IsPristine(info)) { any = true; break; }
        if (!any) return;
        writer.BeginArray("turrets");
        for (var index = 0; index < turrets.Length; index++)
            if (turrets[index] is { } state && set[index] is { } info && !state.IsPristine(info)) state.Describe(writer, index);
        writer.EndArray();
    }

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject(null);
        writer.UInt("id", Id.Value);
        writer.Int("template", Template.Index);
        writer.Text("templateName", Template.Name);
        writer.Int("owner", Owner.Value);
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
        writer.Fixed("z", Z);
        writer.Fixed("angle", Angle);
        writer.UInt("status", Status);
        writer.Int("veterancy", VeterancyLevel);
        writer.Int("experience", Experience);
        writer.Fixed("experienceScalar", ExperienceScalar);
        writer.UInt("weaponBonusConditions", WeaponBonusConditions);
        writer.Bool("dead", EffectivelyDead);
        writer.Bool("slowDeathStarted", SlowDeathStarted);
        writer.Bool("destroyRequested", DestroyRequested);
        writer.Int("spawnTick", SpawnTick);
        writer.UInt("containedBy", ContainedBy.Value);
        CompletedUpgrades.Describe(writer, "completedUpgrades");
        writer.Fixed("speed", Speed);
        writer.Bool("hasMoveTarget", HasMoveTarget);
        writer.Fixed("moveTargetX", MoveTargetX);
        writer.Fixed("moveTargetY", MoveTargetY);
        writer.Int("weaponClip", WeaponClip);
        writer.Int("weaponReadyTick", WeaponReadyTick);
        WeaponSlots?.Describe(writer);
        writer.Int("lastDamageTick", LastDamageTick);
        if (LastDamageTick >= 0) writer.UInt("lastDamageSource", LastDamageSource.Value);
        if (LastDamageHealing) writer.Bool("lastDamageHealing", true);
        writer.Int("lastFireTick", LastFireTick);
        DescribeTurrets(writer);
        Firing?.Describe(writer);
        Acquisition?.Describe(writer);
        if (Captured) writer.Bool("captured", true);
        if (EmpDisabledUntil != 0) writer.Int("empDisabledUntil", EmpDisabledUntil);
        if (ParalyzedUntil != 0) writer.Int("paralyzedUntil", ParalyzedUntil);
        if (Underpowered) writer.Bool("underpowered", true);
        if (ScriptDisableMask != 0) writer.Int("scriptDisableMask", ScriptDisableMask);
        writer.Bool("hasPlayerUpgradeArmor", HasPlayerUpgradeArmor);
        writer.Bool("hasPlayerUpgradeWeapon", HasPlayerUpgradeWeapon);
        writer.Int("constructionWork", ConstructionWork);
        if (ConstructionProgress != 0) writer.Int("constructionProgress", ConstructionProgress);
        writer.UInt("builder", BuilderId.Value);
        writer.Int("sellCompleteTick", SellCompleteTick);
        if (ShroudScalar != Fixed.One) writer.Fixed("shroudScalar", ShroudScalar);
        if (CrateWeaponLevel != 0) writer.Int("crateWeaponLevel", CrateWeaponLevel);
        if (TrainableOverride is { } trainable) writer.Bool("trainable", trainable);
        if (CrateArmorLevel != 0) writer.Int("crateArmorLevel", CrateArmorLevel);
        if (IsInsideFrameStep)
        {
            writer.Fixed("aheadX", AheadX);
            writer.Fixed("aheadY", AheadY);
            writer.Fixed("aheadZ", AheadZ);
            writer.Int("aheadTick", AheadTick);
        }
        if (DestroyNotBefore != 0) writer.Int("destroyNotBefore", DestroyNotBefore);
        if (SpeedPerFrame != Fixed.Zero) writer.Fixed("speedPerFrame", SpeedPerFrame);
        if (HasAheadAngle)
        {
            writer.Fixed("aheadAngle", AheadAngle);
            writer.Int("aheadAngleTick", AheadTick);
        }
        // Written only for an object somebody spied through, so every other object keeps its serialized form (and hash).
        if (VisionSpiedBy is { Count: > 0 })
        {
            writer.BeginArray("visionSpied");
            for (var index = 0; index < VisionSpiedBy.Count; index++)
            {
                writer.BeginObject(null);
                writer.Int("player", VisionSpiedBy.Keys[index]);
                writer.Int("count", VisionSpiedBy.Values[index]);
                writer.EndObject();
            }
            writer.EndArray();
        }
        writer.BeginArray("modules");
        // The hash path writes words down (WordStateWriter); for it the opening of a module, which never changes, is copied from the words prepared when the module was attached.
        var words = writer as WordStateWriter;
        foreach (var module in ModuleArray)
        {
            if (words is not null && module.FrameWords is { } frame) words.Append(frame);
            else DescribeModuleFrame(writer, module);
            module.Describe(writer);
            writer.EndObject();
            writer.EndObject();
        }
        writer.EndArray();
        writer.EndObject();
    }

    /// <summary>
    /// The opening of one module inside <see cref="Describe"/>, up to the module's own state: its index, its name and its tag. None of them changes after the module was attached,
    /// which is why the words this writes may be prepared once (<see cref="SimModule.FrameWords"/>). This routine is the only definition of those words.
    /// </summary>
    internal static void DescribeModuleFrame(IStateWriter writer, SimModule module)
    {
        writer.BeginObject(null);
        writer.Int("index", module.Index);
        writer.Text("name", module.ModuleName);
        writer.Text("tag", module.Tag);
        writer.BeginObject("state");
    }
}

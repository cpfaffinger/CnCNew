using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

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

    /// <summary>Heading in radians.</summary>
    public Fixed Angle { get; internal set; }

    /// <summary>Bits of the object status table (<see cref="ObjectStatusBits"/>).</summary>
    public ulong Status { get; internal set; }

    /// <summary>0 regular, 1 veteran, 2 elite, 3 heroic.</summary>
    public int VeterancyLevel { get; internal set; }

    /// <summary><c>ExperienceTracker::m_currentExperience</c>: raised by enemy kills of trainable objects, set to the level's requirement by explicit level changes.</summary>
    public int Experience { get; internal set; }

    /// <summary><c>ExperienceTracker::m_experienceScalar</c>: multiplier of every experience gain (raised by <c>ExperienceScalarUpgrade</c>).</summary>
    public Fixed ExperienceScalar { get; internal set; } = Fixed.One;

    /// <summary>The set <c>WeaponBonusCondition</c> bits (<c>Object::m_weaponBonusCondition</c>): the veterancy level and <c>PLAYER_UPGRADE</c> are modeled.</summary>
    public uint WeaponBonusConditions { get; internal set; }

    /// <summary>Object-scoped completed-upgrade mask (<c>Object::getObjectCompletedUpgradeMask</c>); most objects never set a bit.</summary>
    public UpgradeMask CompletedUpgrades { get; internal set; }

    /// <summary>Current forward speed (dist/tick); moves along <see cref="Angle"/>. 0 unless the object has an active move order.</summary>
    public Fixed Speed { get; internal set; }

    public bool HasMoveTarget { get; internal set; }
    public Fixed MoveTargetX { get; internal set; }
    public Fixed MoveTargetY { get; internal set; }

    /// <summary>Rounds remaining in the current clip of the object's single (primary) weapon; -1 if the object has none.</summary>
    public int WeaponClip { get; internal set; } = -1;

    /// <summary>Tick at which the weapon may fire again (reload or shot delay, whichever is later).</summary>
    public long WeaponReadyTick { get; internal set; }

    public long LastDamageTick { get; internal set; } = -1;

    /// <summary>Tick of the object's last <c>FireWeaponCommand</c>, for phase-5's coarse "is firing" approximation (-1: never).</summary>
    public long LastFireTick { get; internal set; } = -1;

    /// <summary>Set by <c>ArmorUpgrade</c> once completed: damage resolution reads <see cref="Templates.ObjectTemplate.PlayerUpgradeArmor"/> instead of the default set.</summary>
    public bool HasPlayerUpgradeArmor { get; internal set; }

    /// <summary>Set by <c>WeaponSetUpgrade</c> once completed: combat resolution reads <see cref="Templates.ObjectTemplate.PlayerUpgradeWeapon"/> instead of the default weapon.</summary>
    public bool HasPlayerUpgradeWeapon { get; internal set; }

    /// <summary><c>EFFECTIVELY_DEAD</c>: set by bodies when health reaches zero (always for inactive bodies).</summary>
    public bool EffectivelyDead { get; set; }

    /// <summary><c>DISABLED_EMP</c> until this tick (<c>Object::setDisabledUntil</c> by <c>EMPUpdate</c>/<c>LeafletDropBehavior</c>); 0 when never disabled. Movement and weapons stand still while it lies ahead.</summary>
    public long EmpDisabledUntil { get; internal set; }

    /// <summary><c>SlowDeathBehavior::onDie</c>'s "another AI already handled us" guard, generalized to the entity (not tied to <c>AIUpdateInterface</c>): the weighted sibling lottery runs at most once per death.</summary>
    public bool SlowDeathStarted { get; internal set; }

    public bool DestroyRequested { get; internal set; }
    public long SpawnTick { get; }

    /// <summary>Build ticks already invested while the object is <c>UNDER_CONSTRUCTION</c> (<c>Object::m_constructionPercent</c>, in ticks of its build time).</summary>
    public int ConstructionWork { get; internal set; }

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
    internal UpdateNode[] Updates { get; set; } = [];
    internal List<Timers.SimTimer>? Timers { get; set; }
    internal bool Registered { get; set; }
    internal bool Dirty { get; set; }
    internal ulong CachedHash { get; set; }

    public bool HasStatus(int bit) => (Status & (1UL << bit)) != 0;

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
        writer.Int("lastDamageTick", LastDamageTick);
        writer.Int("lastFireTick", LastFireTick);
        if (EmpDisabledUntil != 0) writer.Int("empDisabledUntil", EmpDisabledUntil);
        writer.Bool("hasPlayerUpgradeArmor", HasPlayerUpgradeArmor);
        writer.Bool("hasPlayerUpgradeWeapon", HasPlayerUpgradeWeapon);
        writer.Int("constructionWork", ConstructionWork);
        writer.UInt("builder", BuilderId.Value);
        writer.Int("sellCompleteTick", SellCompleteTick);
        writer.BeginArray("modules");
        foreach (var module in ModuleArray)
        {
            writer.BeginObject(null);
            writer.Int("index", module.Index);
            writer.Text("name", module.ModuleName);
            writer.Text("tag", module.Tag);
            writer.BeginObject("state");
            module.Describe(writer);
            writer.EndObject();
            writer.EndObject();
        }
        writer.EndArray();
        writer.EndObject();
    }
}

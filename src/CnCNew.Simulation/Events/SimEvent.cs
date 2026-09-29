using CnCNew.Simulation.Ids;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Events;

public enum SimEventKind : byte
{
    EntitySpawned = 1,
    EntityDamaged = 2,
    EntityDied = 3,
    EntityDestroyed = 4,
    TimerFired = 5,
    /// <summary>A module asked for a visual/audio effect list; executed by presentation, never by the simulation.</summary>
    FxRequested = 6,
    /// <summary>A module asked for an object creation list; executed by the rules layer (phase 5).</summary>
    CreationListRequested = 7,
    /// <summary>A module asked for a weapon to be fired (phase 5).</summary>
    WeaponFireRequested = 8,
    CommandRejected = 9,

    // ------------------------------------------------------------------------------------------------- phase 5

    /// <summary>An upgrade module completed (<c>Value</c>: the <see cref="Templates.UpgradeInfo.Index"/>).</summary>
    UpgradeCompleted = 10,
    /// <summary><c>FireWeaponCommand</c> resolved (<c>Other</c>: the target, <c>Value</c>: the adjusted damage dealt).</summary>
    WeaponFired = 11,
    /// <summary>A move order reached its target (<c>AIUpdateInterface</c>).</summary>
    MoveArrived = 12,
    /// <summary><c>ProductionUpdate</c> completed a queue entry and requested a spawn (<c>Value</c>: the template index; the resulting spawn raises its own <see cref="EntitySpawned"/>).</summary>
    ProductionCompleted = 13,
    /// <summary>The last player without any remaining objects was defeated (<c>Player</c>: the defeated player).</summary>
    PlayerDefeated = 14,
    /// <summary><c>ActiveBody::attemptDamage</c>'s fear-sound roll succeeded when crossing the 25% health threshold; presentation-only (the roll itself, win or lose, is already consumed from <c>LogicRandom</c>).</summary>
    VoiceFearRequested = 15,
    /// <summary><c>PurchaseScienceCommand</c> completed (<c>Value</c>: the <see cref="Templates.ScienceInfo.Index"/>).</summary>
    ScienceCompleted = 16,
    /// <summary><c>ContainModuleInterface::addToContain</c> (<c>Entity</c>: the entity that entered, <c>Other</c>: the container).</summary>
    EntityContained = 17,
    /// <summary><c>ContainModuleInterface::removeFromContain</c> (<c>Entity</c>: the entity that left, <c>Other</c>: the container it left).</summary>
    EntityUncontained = 18,
    /// <summary><c>CreateCrateDie</c> chose a crate object (<c>Entity</c>: the dead object, <c>Text</c>: the crate template, <c>Value</c>: the owning player id, 0 = neutral). Raised whether or not the crate template is instantiable yet.</summary>
    CrateRequested = 19,
    /// <summary>
    /// An object creation list executed only partially (<c>Entity</c>: the source, <c>Text</c>: the nugget kind, template or list name,
    /// <c>Value</c>: <see cref="CreationSkipReasons"/>). Never a silent drop: every skipped nugget or member raises exactly one event.
    /// </summary>
    CreationSkipped = 20,

    /// <summary>
    /// A special power was used (<c>Entity</c>: the entity holding the power module, <c>Other</c>: the target object or invalid,
    /// <c>Text</c>: the <c>SpecialPower</c> name, <c>Value</c>: the special-power index).
    /// </summary>
    SpecialPowerUsed = 21,

    /// <summary>
    /// A supply truck emptied its boxes at a supply center (<c>Entity</c>: the center, <c>Other</c>: the truck, <c>Value</c>: the credited
    /// amount as a plain integer).
    /// </summary>
    SupplyDeposited = 22,

    /// <summary>A construction site appeared (<c>Entity</c>: the site, <c>Other</c>: the builder, <c>Value</c>: the paid cost as a plain integer).</summary>
    ConstructionStarted = 23,

    /// <summary>The last unit of construction work was done and the structure is complete (<c>Entity</c>: the structure, <c>Other</c>: the builder).</summary>
    ConstructionCompleted = 24,

    /// <summary>A sale finished and the structure is gone (<c>Entity</c>: the structure, <c>Value</c>: the credited amount as a plain integer).</summary>
    StructureSold = 25,

    /// <summary>An unfinished structure was cancelled and its full cost refunded (<c>Entity</c>: the site, <c>Value</c>: the refunded amount as a plain integer).</summary>
    ConstructionCancelled = 26,
}

/// <summary>
/// An immutable world event. Field meaning per kind: <c>Entity</c> is the subject, <c>Other</c> the source/target,
/// <c>Text</c> a template, effect or reason name, <c>Value</c> an amount (raw fixed-point), timer tag or reject code.
/// </summary>
public readonly record struct SimEvent(EventId Id, long Tick, SimEventKind Kind, EntityId Entity, EntityId Other, PlayerId Player, string Text, long Value)
{
    public void Describe(IStateWriter writer)
    {
        writer.BeginObject(null);
        writer.UInt("id", Id.Value);
        writer.Int("tick", Tick);
        writer.Int("kind", (int)Kind);
        writer.UInt("entity", Entity.Value);
        writer.UInt("other", Other.Value);
        writer.Int("player", Player.Value);
        writer.Text("text", Text);
        writer.Int("value", Value);
        writer.EndObject();
    }

    public static SimEvent Load(IStateReader reader)
    {
        var kind = reader.Int("kind");
        if (!Enum.IsDefined(typeof(SimEventKind), (byte)kind)) throw new InvalidDataException($"Unknown event kind {kind}.");
        return new SimEvent(new EventId(reader.UInt("id")), reader.Int("tick"), (SimEventKind)kind,
            new EntityId(checked((uint)reader.UInt("entity"))), new EntityId(checked((uint)reader.UInt("other"))),
            new PlayerId(checked((int)reader.Int("player"))), reader.Text("text"), reader.Int("value"));
    }
}

/// <summary>Reason codes of <see cref="SimEventKind.CreationSkipped"/>.</summary>
public static class CreationSkipReasons
{
    /// <summary>The nugget kind (debris, force, delivery, attack) has no simulation model yet; <c>Text</c> is the nugget kind.</summary>
    public const int NuggetNotSimulated = 1;
    /// <summary>A pickable object exists but is not instantiable (unimplemented modules); <c>Text</c> is the template.</summary>
    public const int TemplateNotInstantiable = 2;
    /// <summary>The <c>PutInContainer</c> template is not instantiable; the whole nugget is skipped; <c>Text</c> is the template.</summary>
    public const int ContainerNotInstantiable = 3;
    /// <summary>The named object template does not exist; <c>Text</c> is the name.</summary>
    public const int UnknownTemplate = 4;
    /// <summary>The named creation list does not exist; <c>Text</c> is the name.</summary>
    public const int UnknownList = 5;
    /// <summary>A <c>CreateObject</c> nugget names no object at all; <c>Text</c> is the nugget kind.</summary>
    public const int NoObjectNamed = 6;
    /// <summary>A nugget setting (<c>InvulnerableTime</c>, <c>DiesOnBadLand</c>) has no simulation model yet and was ignored; <c>Text</c> is the setting name.</summary>
    public const int SettingNotSimulated = 7;
    /// <summary>An <c>OCLSpecialPower</c> creates its list at a map edge (<c>EDGE_NEAR_SOURCE</c>/<c>EDGE_NEAR_TARGET</c>/<c>EDGE_FARTHEST_FROM_TARGET</c>, or a gunship's edge approach) and no terrain model exists; <c>Text</c> is the create location.</summary>
    public const int NoTerrainEdge = 8;
    /// <summary>A <c>SpawnBehavior</c> owner has no production-exit module, so nothing can leave it; <c>Text</c> is the first spawn template.</summary>
    public const int NoExitInterface = 9;
}

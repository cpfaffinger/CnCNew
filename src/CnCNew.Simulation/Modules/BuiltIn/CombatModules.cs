using System.Collections.Immutable;
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
/// Weapon-driven modules that need no targeting: <c>FireWeaponUpdate</c> (a private weapon that detonates at its owner's own position,
/// e.g. poison fields, sneak-attack shockwaves) and <c>ReplaceObjectUpgrade</c> (an upgrade that swaps the object for another
/// template). Sources: <c>FireWeaponUpdate.cpp</c> <c>update</c>/<c>isOkayToFire</c>, <c>ReplaceObjectUpgrade.cpp</c>
/// <c>upgradeImplementation</c>; the rules and simplifications are in docs/GAME-RULES.md (section 6g).
/// </summary>
internal static class CombatModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<FireWeaponUpdateData>("FireWeaponUpdate", ModuleOwnerKind.Object, (configuration, _) => FireWeaponUpdateData.Parse(configuration), data => new FireWeaponUpdate(data)),
        new ModuleBinding<ReplaceObjectUpgradeData>("ReplaceObjectUpgrade", ModuleOwnerKind.Object, ReplaceObjectUpgradeData.Parse, data => new ReplaceObjectUpgrade(data)),
    ];
}

/// <summary><c>FireWeaponUpdateModuleData</c> field set; both delays are <c>parseDurationUnsignedInt</c> (whole ticks, default 0).</summary>
public sealed record FireWeaponUpdateData(string Weapon, long InitialDelayTicks, long ExclusiveWeaponDelayTicks) : ModuleData
{
    public static FireWeaponUpdateData Parse(ModuleConfiguration configuration) => new(
        ModuleConfigReader.Text(configuration, "Weapon"),
        ModuleConfigReader.Ticks(configuration, "InitialDelay", 0),
        ModuleConfigReader.Ticks(configuration, "ExclusiveWeaponDelay", 0));
}

/// <summary>
/// <c>FireWeaponUpdate::update</c>: after <c>InitialDelay</c> the module's private weapon (own clip and reload state, never the object's
/// weapon set) detonates at the object's position whenever it is ready — <c>forceFireWeapon</c> ignores range and has no victim, so the
/// weapon's radius decides who is hurt. Not while under construction, and not within <c>ExclusiveWeaponDelay</c> ticks after the object
/// fired one of its own weapons (<c>Object::getLastShotFiredFrame</c> looks at the weapon set only, so this weapon never suppresses itself).
/// </summary>
internal sealed class FireWeaponUpdate(FireWeaponUpdateData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private long _initialDelayTick;
    private long _readyTick;
    private int _clip;

    public override string ModuleName => "FireWeaponUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        if (data.Weapon.Length != 0)
        {
            if (!world.Weapons.TryGet(data.Weapon, out var weapon)) throw new InvalidDataException($"FireWeaponUpdate references the unknown weapon '{data.Weapon}'.");
            _clip = ClipCapacity(weapon);
        }
        _initialDelayTick = world.Tick + data.InitialDelayTicks;
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public long Update(IWorldServices world)
    {
        if (world.Tick < _initialDelayTick || data.Weapon.Length == 0) return SimulationTiming.SleepNone;
        if (world.Tick < _readyTick) return SimulationTiming.SleepNone;
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return SimulationTiming.SleepNone;
        if (data.ExclusiveWeaponDelayTicks > 0 && Entity.LastFireTick >= 0 && world.Tick < Entity.LastFireTick + data.ExclusiveWeaponDelayTicks) return SimulationTiming.SleepNone;

        var weapon = world.Weapons.TryGet(data.Weapon, out var info) ? info : throw new InvalidDataException($"FireWeaponUpdate references the unknown weapon '{data.Weapon}'.");
        if (_clip == 0) _clip = ClipCapacity(weapon);
        var bonuses = world.Templates.GlobalWeaponBonus;
        var conditions = Entity.WeaponBonusConditions;
        if (_clip != int.MaxValue) _clip--;
        _readyTick = world.Tick + (_clip == 0 ? weapon.ClipReloadWith(bonuses, conditions) : weapon.DelayBetweenShotsWith(bonuses, conditions));
        var damage = world.DealWeaponDamage(Entity, weapon, null, Entity.X, Entity.Y, Entity.Z);
        world.Emit(SimEventKind.WeaponFired, Entity.Id, EntityId.Invalid, weapon.Name, damage.Raw);
        return SimulationTiming.SleepNone;
    }

    private static int ClipCapacity(WeaponInfo weapon) => weapon.ClipSize == 0 ? int.MaxValue : weapon.ClipSize;

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("initialDelay", _initialDelayTick);
        writer.Int("ready", _readyTick);
        writer.Int("clip", _clip);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _initialDelayTick = reader.Int("initialDelay");
        _readyTick = reader.Int("ready");
        _clip = checked((int)reader.Int("clip"));
    }
}

public sealed record ReplaceObjectUpgradeData(UpgradeMuxData Mux, string ReplaceObject) : ModuleData
{
    public static ReplaceObjectUpgradeData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog)
        => new(UpgradeMuxData.Parse(configuration, catalog), ModuleConfigReader.Text(configuration, "ReplaceObject"));
}

/// <summary>
/// <c>ReplaceObjectUpgrade::upgradeImplementation</c>: the object is destroyed and a fresh one of the replacement template takes its
/// owner, position and orientation (the replacement is a new object: full health, no veterancy, no queued production). A template
/// that is unknown or not instantiable makes the upgrade do nothing beyond a <see cref="SimEventKind.CreationSkipped"/> event (the
/// reference asserts and returns before destroying anything). Not modelled: the pathfind-map bookkeeping and
/// <c>Player::onStructureConstructionComplete</c> (no such player statistics exist).
/// </summary>
internal sealed class ReplaceObjectUpgrade(ReplaceObjectUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "ReplaceObjectUpgrade";

    protected override void Activate(IWorldServices world)
    {
        if (!world.Templates.TryGet(data.ReplaceObject, out var template))
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.ReplaceObject, CreationSkipReasons.UnknownTemplate);
            return;
        }
        if (!template.IsInstantiable)
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.ReplaceObject, CreationSkipReasons.TemplateNotInstantiable);
            return;
        }
        world.RequestDestroy(Entity.Id);
        world.RequestSpawn(template.Index, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle);
    }
}

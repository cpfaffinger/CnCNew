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
        new ModuleBinding<CrushDieData>("CrushDie", ModuleOwnerKind.Object, (configuration, _) => CrushDieData.Parse(configuration), data => new CrushDie(data)),
    ];
}

/// <summary><c>FireWeaponUpdateModuleData</c> field set; both delays are <c>parseDurationUnsignedInt</c> (whole ticks, default 0).</summary>
public sealed record FireWeaponUpdateData(string Weapon, long InitialDelayTicks, long ExclusiveWeaponDelayTicks) : ModuleData
{
    public static FireWeaponUpdateData Parse(RatedModuleConfiguration configuration) => new(
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
        // The first update comes one logic frame of the original after the object appeared (at 30 Hz the next tick): the first shot of a field is an event, not a step of motion.
        _nextWake = world.Tick + world.Time.TicksPerOriginalFrame;
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
        // A ClipReloadTime of 0 reloads at once; the next shot then comes with the next logic frame of the original, not with the next tick.
        _readyTick = world.Tick + (_clip == 0 ? world.Time.ZeroIsNextFrame(weapon.ClipReloadWith(world.Time, bonuses, conditions)) : weapon.DelayBetweenShotsWith(world.Time, bonuses, conditions));
        var damage = world.FireWeaponAt(Entity, weapon, null, Entity.X, Entity.Y, Entity.Z);
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

/// <summary><c>CrushDieModuleData</c>: per crushed end a sound and how often it plays.</summary>
public sealed record CrushDieData(DieMuxData Mux, string TotalSound, string BackSound, string FrontSound, int TotalPercent, int BackPercent, int FrontPercent) : DieData(Mux)
{
    public static CrushDieData Parse(RatedModuleConfiguration configuration) => new(DieMuxData.Parse(configuration),
        ModuleConfigReader.Text(configuration, "TotalCrushSound"), ModuleConfigReader.Text(configuration, "BackEndCrushSound"), ModuleConfigReader.Text(configuration, "FrontEndCrushSound"),
        checked((int)ModuleConfigReader.Int(configuration, "TotalCrushSoundPercent", 0)), checked((int)ModuleConfigReader.Int(configuration, "BackEndCrushSoundPercent", 0)),
        checked((int)ModuleConfigReader.Int(configuration, "FrontEndCrushSoundPercent", 0)));
}

/// <summary>
/// <c>CrushDie::onDie</c> reacts to <c>DAMAGE_CRUSH</c>: of the victim's middle, front and back point the one nearest the crusher (<c>crushLocationCheck</c>; no crusher: all of it) names the sound,
/// which plays with its percent, drawn from the logic stream only when the end has a sound. The crushed ends a car shows are the presentation's (<c>UnitConditionTracker</c>): a victim dies once, so nothing
/// of it was crushed before.
/// </summary>
internal sealed class CrushDie(CrushDieData data) : DieModuleBase(data.Mux)
{
    private static readonly int CrushDamage = NameTables.DamageType.TryIndexOf("CRUSH", out var crush) ? crush : throw new InvalidOperationException("CRUSH is not a damage type.");

    /// <summary>"be sure that 0==never, 100==always".</summary>
    private const int HighestPercent = 99;

    public override string ModuleName => "CrushDie";

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!Mux.IsApplicable(Entity, info) || info.DamageType != CrushDamage) return;
        var (sound, percent) = world.FindEntity(info.Source) is { } crusher ? EndNearest(crusher) : (data.TotalSound, data.TotalPercent);
        if (sound.Length == 0) return;
        if (world.LogicRandom.Range(0, HighestPercent) < percent) world.Emit(SimEventKind.CrushSound, Entity.Id, info.Source, sound, 0);
    }

    private (string Sound, int Percent) EndNearest(Entity crusher)
    {
        var offset = Entity.Template.GeometryMajorRadius / Fixed.FromInt(2);
        var (offsetX, offsetY) = (FixedTrig.Cos(Entity.Angle) * offset, FixedTrig.Sin(Entity.Angle) * offset);
        Fixed Squared(Fixed x, Fixed y) => (x - crusher.X) * (x - crusher.X) + (y - crusher.Y) * (y - crusher.Y);
        var (middle, front, back) = (Squared(Entity.X, Entity.Y), Squared(Entity.X + offsetX, Entity.Y + offsetY), Squared(Entity.X - offsetX, Entity.Y - offsetY));
        if (back < middle && back < front) return (data.BackSound, data.BackPercent);
        return front < middle ? (data.FrontSound, data.FrontPercent) : (data.TotalSound, data.TotalPercent);
    }
}

public sealed record ReplaceObjectUpgradeData(UpgradeMuxData Mux, string ReplaceObject) : ModuleData
{
    public static ReplaceObjectUpgradeData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog)
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

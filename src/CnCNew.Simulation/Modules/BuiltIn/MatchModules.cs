using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The modules that keep the retail reference match units and structures from being instantiable
/// (<c>docs/GAME-RULES.md</c> §7a, <c>SimTool reference-match</c> blocker report). Each one is bound with its full strict
/// schema; whatever part of its behaviour needs a subsystem that does not exist yet is stated at the module, never skipped silently.
/// </summary>
internal static class MatchModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<NoFieldsData>("BaseRegenerateUpdate", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new BaseRegenerateUpdate()),
    ];
}

/// <summary>Data of a module whose reference field table is empty.</summary>
public sealed record NoFieldsData : ModuleData
{
    public static NoFieldsData Instance { get; } = new();
}

// ========================================================================================== BaseRegenerateUpdate

/// <summary>
/// <c>BaseRenerateUpdate.cpp</c> (sic): structures heal <c>BaseRegenHealthPercentPerSecond</c> of their maximum health per second
/// (<c>GameData.ini</c>) once they have been undamaged for <c>BaseRegenDelay</c>. The module has no keys of its own. Ported as
/// written: it sleeps while at full health, wakes <see cref="IWorldServices.BaseRegenDelayTicks"/> after every hit, heals in
/// bursts of three ticks' worth every three ticks, waits out construction and stops for good once <c>SOLD</c>. The reference's
/// "a healing hit parks the module" branch cannot occur here because <see cref="IDamageModule.OnDamage"/> only ever sees health-lowering damage.
/// </summary>
public sealed class BaseRegenerateUpdate : SimModule, IUpdateModule, IDamageModule
{
    private const int HealRate = 3;
    private static readonly int SoldBit = ObjectStatusBits.Index("SOLD");
    private long _nextWake = -1;

    public override string ModuleName => "BaseRegenerateUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
        => _nextWake = world.BaseRegenHealthPercentPerSecond == Fixed.Zero ? SimulationTiming.SleepForever : world.Tick + SimulationTiming.SleepNone;

    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt)
    {
        if (world.BaseRegenHealthPercentPerSecond > Fixed.Zero && info.DamageType != DamageTypes.Healing)
            world.WakeAt(Entity, this, world.Tick + Math.Max(world.BaseRegenDelayTicks, SimulationTiming.SleepNone));
        else
            world.WakeAt(Entity, this, SimulationTiming.SleepForever);
    }

    public long Update(IWorldServices world)
    {
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return SimulationTiming.SleepNone;
        if (Entity.HasStatus(SoldBit)) return SimulationTiming.SleepForever;
        var body = Entity.Body;
        if (body is null || body.MaxHealth == body.Health) return SimulationTiming.SleepForever;
        var amount = body.MaxHealth * world.BaseRegenHealthPercentPerSecond * Fixed.FromInt(HealRate) / Fixed.FromInt(SimulationTiming.LogicFramesPerSecond);
        world.Damage(Entity.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
        return HealRate;
    }

    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

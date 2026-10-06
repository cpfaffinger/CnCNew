using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The repair/heal pair: <c>RepairDockUpdate</c> (a dock that heals the docked object over <c>TimeForFullHeal</c>) and
/// <c>AutoFindHealingUpdate</c> (computer-controlled infantry that walk to a heal pad when hurt). Sources: <c>RepairDockUpdate.cpp</c>
/// <c>action</c>, <c>AutoFindHealingUpdate.cpp</c> <c>update</c>/<c>scanClosestTarget</c>, <c>ActionManager.cpp canGetHealedAt</c>,
/// <c>AIUpdate.cpp privateGetHealed</c>; the rules and simplifications are in docs/GAME-RULES.md (section 6f).
/// </summary>
internal static class RepairModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<RepairDockData>("RepairDockUpdate", ModuleOwnerKind.Object, (configuration, _) => RepairDockData.Parse(configuration), data => new RepairDockUpdate(data)),
        new ModuleBinding<AutoFindHealingData>("AutoFindHealingUpdate", ModuleOwnerKind.Object, (configuration, _) => AutoFindHealingData.Parse(configuration), data => new AutoFindHealingUpdate(data)),
    ];
}

/// <summary><c>RepairDockUpdate.cpp</c> field set; <c>TimeForFullHeal</c> is <c>parseDurationReal</c> (fractional frames, reference default one frame).</summary>
public sealed record RepairDockData(int NumberApproachPositions, Fixed FramesForFullHeal) : DockData(NumberApproachPositions)
{
    public static RepairDockData Parse(RatedModuleConfiguration configuration)
    {
        var field = ModuleConfigReader.Find(configuration, "TimeForFullHeal");
        var frames = field is null
            ? Fixed.FromInt(configuration.Time.TicksPerOriginalFrame) // m_framesForFullHeal = 1.0f (RepairDockUpdate.cpp): one logic frame of the original
            : field.Value.Value is GameTime time ? configuration.Time.DurationMillisecondsToFixedTicks(time.Milliseconds) : throw new InvalidDataException("RepairDockUpdate TimeForFullHeal is not a duration.");
        if (frames <= Fixed.Zero) throw new InvalidDataException("RepairDockUpdate TimeForFullHeal must be positive.");
        return new RepairDockData(ParseApproachPositions(configuration), frames);
    }
}

/// <summary>
/// <c>RepairDockUpdate::action</c>: the first action for a new docker fixes the per-tick amount as <c>(max - health) / framesForFullHeal</c>, every
/// action heals that amount (source: this dock, <c>DAMAGE_HEALING</c>) and the docking completes once the docker is at full health. The
/// reference's <c>m_lastRepair</c> is the "amount already fixed" flag (<c>INVALID_ID</c> is 0 there). Not modelled: the drone healing
/// (the docker's battle drone is healed to full on each action; drones do not exist here).
/// </summary>
internal sealed class RepairDockUpdate(RepairDockData data) : DockUpdateBase(data)
{
    private EntityId _lastRepair;
    private Fixed _healthToAddPerTick;

    public override string ModuleName => "RepairDockUpdate";

    public override bool Action(IWorldServices world, Entity docker)
    {
        if (docker.Body is not { } body) return false;
        if (!_lastRepair.IsValid)
        {
            _lastRepair = docker.Id;
            _healthToAddPerTick = world.Time.SharePerTick(body.MaxHealth - body.Health, data.FramesForFullHeal);
        }
        if (body.Health >= body.MaxHealth)
        {
            _lastRepair = EntityId.Invalid;
            return false;
        }
        // A step below one fixed-point unit per tick would never finish; then the last step takes what is left.
        var amount = _healthToAddPerTick > Fixed.Zero ? _healthToAddPerTick : body.MaxHealth - body.Health;
        world.Damage(docker.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
        world.MarkDirty(docker);
        return true;
    }

    protected override void DescribeDock(IStateWriter writer)
    {
        writer.UInt("lastRepair", _lastRepair.Value);
        writer.Int("healthPerTick", _healthToAddPerTick.Raw);
    }

    protected override void LoadDock(IStateReader reader)
    {
        _lastRepair = new EntityId(checked((uint)reader.UInt("lastRepair")));
        _healthToAddPerTick = Fixed.FromRaw(reader.Int("healthPerTick"));
    }
}

/// <summary><c>AutoFindHealingUpdate.cpp</c> field set with the reference constructor defaults (scan every frame, range 0, never above 95 %, always below 25 %).</summary>
public sealed record AutoFindHealingData(long ScanTicks, Fixed ScanRange, Fixed NeverHeal, Fixed AlwaysHeal) : ModuleData
{
    public static AutoFindHealingData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "ScanRate", 0),
        ModuleConfigReader.Real(configuration, "ScanRange", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "NeverHeal", Fixed.FromDecimal(0.95m)),
        ModuleConfigReader.Real(configuration, "AlwaysHeal", Fixed.FromDecimal(0.25m)));
}

/// <summary>
/// <c>AutoFindHealingUpdate::update</c>: only for computer players. A countdown of <c>ScanRate</c> ticks separates two scans; a scan is
/// skipped when the object is healthier than <c>NeverHeal</c> or busy (the reference's "keep at it above <c>AlwaysHeal</c>" branch sits
/// behind an early return, so <c>AlwaysHeal</c> is read but has no effect). The scan takes the closest object with <c>KINDOF_HEAL_PAD</c>
/// within <c>ScanRange</c> (centre distance, ties by id, regardless of who owns it) and orders <see cref="AIUpdateInterface.OrderGetHealed"/>,
/// which only proceeds for an allied, complete, living pad and a hurt infantry object.
/// </summary>
internal sealed class AutoFindHealingUpdate(AutoFindHealingData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private long _nextScanTicks;

    public override string ModuleName => "AutoFindHealingUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public long Update(IWorldServices world)
    {
        if (!world.IsComputerPlayer(Entity.Owner)) return SimulationTiming.SleepNone;
        if (_nextScanTicks > 0)
        {
            _nextScanTicks--;
            return SimulationTiming.SleepNone;
        }
        // m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames (AutoFindHealingUpdate.cpp): a period of ScanRate + 1 frames of the original.
        _nextScanTicks = world.Time.CountdownReload(data.ScanTicks);

        var ai = Entity.Modules.OfType<AIUpdateInterface>().FirstOrDefault();
        if (ai is null || Entity.Body is not { } body) return SimulationTiming.SleepNone;
        if (body.Health > body.MaxHealth * data.NeverHeal) return SimulationTiming.SleepNone;
        if (!ai.IsIdle) return SimulationTiming.SleepNone;

        if (ScanClosestHealPad(world) is { } pad) ai.OrderGetHealed(world, pad, false);
        return SimulationTiming.SleepNone;
    }

    private Entity? ScanClosestHealPad(IWorldServices world)
    {
        Entity? best = null;
        var bestDistance = Fixed.Zero;
        var rangeSquared = data.ScanRange * data.ScanRange;
        foreach (var candidate in world.Entities)
        {
            if (!candidate.Template.IsKindOf("HEAL_PAD")) continue;
            var dx = candidate.X - Entity.X;
            var dy = candidate.Y - Entity.Y;
            var distance = dx * dx + dy * dy;
            if (distance > rangeSquared) continue;
            if (best is null || distance < bestDistance || (distance == bestDistance && candidate.Id.Value < best.Id.Value))
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("nextScan", _nextScanTicks);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _nextScanTicks = reader.Int("nextScan");
    }
}
